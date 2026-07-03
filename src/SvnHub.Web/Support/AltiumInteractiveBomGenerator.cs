using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Eda.Primitives;

namespace SvnHub.Web.Support;

public sealed partial class AltiumInteractiveBomGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public string GenerateGenericJson(
        byte[] pcbDocBytes,
        string title,
        IReadOnlyDictionary<string, AltiumBomRow>? schematicBomRows = null)
    {
        ArgumentNullException.ThrowIfNull(pcbDocBytes);

        using var input = new MemoryStream(pcbDocBytes, writable: false);
        var board = new PcbDocReader().Read(input);
        var components = board.Components.OfType<PcbComponent>().ToArray();
        var footprints = new JsonArray();
        var bomComponents = new JsonArray();
        var allPoints = new List<(double X, double Y)>();

        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            var designator = GetComponentReference(component, i);
            if (schematicBomRows is not null && !schematicBomRows.ContainsKey(designator))
            {
                continue;
            }

            var bomRow = CreateBomRow(component, i, schematicBomRows);
            var lookup = CreateComponentLookup(bomRow);
            var value = ResolveComponentValue(bomRow, lookup);
            var extraFields = ResolveExtraFields(bomRow.Parameters, lookup);
            var center = ToIbomPoint(new CoordPoint(component.X, component.Y));
            allPoints.Add((center[0]!.GetValue<double>(), center[1]!.GetValue<double>()));

            var pads = new JsonArray();
            foreach (var pad in component.Pads.OfType<PcbPad>())
            {
                pads.Add(CreatePad(board, pad));
                var padPoint = ToIbomPoint(pad.Location);
                allPoints.Add((padPoint[0]!.GetValue<double>(), padPoint[1]!.GetValue<double>()));
            }

            var bbox = CreateFootprintBoundingBox(board, component, center);
            footprints.Add(new JsonObject
            {
                ["ref"] = bomRow.Designator,
                ["center"] = center,
                ["bbox"] = bbox,
                ["pads"] = pads,
                ["drawings"] = CreateFootprintCopperDrawings(component),
                ["layer"] = IsBottom(component.Layer) || component.FlippedOnLayer ? "B" : "F",
            });

            bomComponents.Add(new JsonObject
            {
                ["ref"] = bomRow.Designator,
                ["val"] = value,
                ["footprint"] = bomRow.Footprint,
                ["layer"] = IsBottom(component.Layer) || component.FlippedOnLayer ? "B" : "F",
                ["extra_fields"] = extraFields,
            });
        }

        var layerDrawings = CreateLayerDrawings(board, schematicBomRows);
        var edges = CreateBoardEdges(board, allPoints);
        var bounds = BoundsFromEdges(edges) ?? BoundsFromPoints(allPoints);

        var pcbdata = new JsonObject
        {
            ["edges_bbox"] = new JsonObject
            {
                ["minx"] = Round(bounds.MinX),
                ["miny"] = Round(bounds.MinY),
                ["maxx"] = Round(bounds.MaxX),
                ["maxy"] = Round(bounds.MaxY),
            },
            ["edges"] = edges,
            ["drawings"] = layerDrawings,
            ["footprints"] = footprints,
            ["tracks"] = CreateTracks(board),
            ["zones"] = CreateZones(board),
            ["nets"] = new JsonArray(board.Nets.Select(n => JsonValue.Create(n.Name ?? "")).ToArray<JsonNode?>()),
            ["metadata"] = new JsonObject
            {
                ["title"] = title,
                ["revision"] = "",
                ["company"] = "",
                ["date"] = "",
            },
        };

        return new JsonObject
        {
            ["spec_version"] = 1,
            ["pcbdata"] = pcbdata,
            ["components"] = bomComponents,
        }.ToJsonString(JsonOptions);
    }

    private static JsonObject CreatePad(PcbDocument board, PcbPad pad)
    {
        var size = GetPadSize(pad);
        var shape = GetPadShape(pad);
        var result = new JsonObject
        {
            ["layers"] = CreatePadLayers(pad),
            ["pos"] = ToIbomPoint(pad.Location),
            ["size"] = new JsonArray(Round(size.X), Round(size.Y)),
            ["angle"] = AngleForIbom(pad.Rotation),
            ["shape"] = shape,
            ["type"] = pad.HoleSize.ToRaw() > 0 ? "th" : "smd",
        };

        if (shape == "roundrect")
        {
            result["radius"] = Round(Math.Min(size.X, size.Y) * 0.15);
        }

        if (pad.HoleSize.ToRaw() > 0)
        {
            result["drillshape"] = "circle";
            result["drillsize"] = new JsonArray(Round(pad.HoleSize.ToMils()), 0);
        }

        var net = GetNetName(board, pad.NetIndex, pad.Net);
        if (!string.IsNullOrWhiteSpace(net))
        {
            result["net"] = net;
        }

        if (string.Equals(pad.Designator?.Trim(), "1", StringComparison.Ordinal))
        {
            result["pin1"] = 1;
        }

        return result;
    }

    private static JsonObject CreateTracks(PcbDocument board)
    {
        var front = new JsonArray();
        var back = new JsonArray();

        foreach (var track in board.Tracks.OfType<PcbTrack>())
        {
            var net = GetNetName(board, track.NetIndex, track.Net);
            if (string.IsNullOrWhiteSpace(net))
            {
                continue;
            }

            var item = new JsonObject
            {
                ["start"] = ToIbomPoint(track.Start),
                ["end"] = ToIbomPoint(track.End),
                ["width"] = Round(track.Width.ToMils()),
                ["net"] = net,
            };

            if (IsBottom(track.Layer))
            {
                back.Add(item);
            }
            else if (IsTop(track.Layer))
            {
                front.Add(item);
            }
        }

        foreach (var arc in board.Arcs.OfType<PcbArc>())
        {
            var net = GetNetName(board, arc.NetIndex, arc.Net);
            if (string.IsNullOrWhiteSpace(net))
            {
                continue;
            }

            var item = CreateArcTrack(arc);
            item["net"] = net;

            if (IsBottom(arc.Layer))
            {
                back.Add(item);
            }
            else if (IsTop(arc.Layer))
            {
                front.Add(item);
            }
        }

        foreach (var via in board.Vias.OfType<PcbVia>())
        {
            var pos = ToIbomPoint(via.Location);
            var item = new JsonObject
            {
                ["start"] = pos.DeepClone(),
                ["end"] = pos.DeepClone(),
                ["width"] = Round(via.Diameter.ToMils()),
                ["drillsize"] = Round(via.HoleSize.ToMils()),
            };

            var net = GetNetName(board, via.NetIndex, via.Net);
            if (!string.IsNullOrWhiteSpace(net))
            {
                item["net"] = net;
            }

            front.Add(item.DeepClone());
            back.Add(item);
        }

        foreach (var pad in board.Pads.OfType<PcbPad>())
        {
            if (pad.ComponentIndex >= 0 ||
                pad.HoleSize.ToRaw() <= 0 ||
                !string.Equals(GetPadShape(pad), "circle", StringComparison.Ordinal))
            {
                continue;
            }

            var pos = ToIbomPoint(pad.Location);
            var size = GetPadSize(pad);
            var item = new JsonObject
            {
                ["start"] = pos.DeepClone(),
                ["end"] = pos.DeepClone(),
                ["width"] = Round(Math.Max(size.X, size.Y)),
                ["drillsize"] = Round(pad.HoleSize.ToMils()),
            };

            var net = GetNetName(board, pad.NetIndex, pad.Net);
            if (!string.IsNullOrWhiteSpace(net))
            {
                item["net"] = net;
            }

            front.Add(item.DeepClone());
            back.Add(item);
        }

        return new JsonObject
        {
            ["F"] = front,
            ["B"] = back,
        };
    }

    private static JsonObject CreateArcTrack(PcbArc arc)
    {
        var startEnd = ArcAnglesForIbom(arc.StartAngle, arc.EndAngle);
        return new JsonObject
        {
            ["center"] = ToIbomPoint(arc.Center),
            ["radius"] = Round(arc.Radius.ToMils()),
            ["startangle"] = IsFullCircleArc(arc) ? 0 : startEnd.Start,
            ["endangle"] = IsFullCircleArc(arc) ? 360 : startEnd.End,
            ["width"] = Round(arc.Width.ToMils()),
        };
    }

    private static JsonObject CreateLayerDrawings(
        PcbDocument board,
        IReadOnlyDictionary<string, AltiumBomRow>? schematicBomRows)
    {
        var frontSilkscreen = new JsonArray();
        var backSilkscreen = new JsonArray();
        var frontFabrication = new JsonArray();
        var backFabrication = new JsonArray();

        foreach (var track in board.Tracks.OfType<PcbTrack>())
        {
            if (IsEdgeLayer(board, track.Layer))
            {
                continue;
            }

            AddDrawingToBucket(CreateTrackDrawing(track), track.Layer);
        }

        foreach (var arc in board.Arcs.OfType<PcbArc>())
        {
            if (IsEdgeLayer(board, arc.Layer))
            {
                continue;
            }

            AddDrawingToBucket(CreateArcDrawing(arc), arc.Layer);
        }

        foreach (var fill in board.Fills.OfType<PcbFill>())
        {
            AddDrawingToBucket(CreateFillDrawing(fill), fill.Layer);
        }

        foreach (var region in board.Regions.OfType<PcbRegion>())
        {
            var drawing = CreateRegionDrawing(region);
            if (drawing is not null)
            {
                AddDrawingToBucket(drawing, region.Layer);
            }
        }

        foreach (var text in board.Texts.OfType<PcbText>())
        {
            if (IsEdgeLayer(board, text.Layer))
            {
                continue;
            }

            var drawing = CreateTextDrawing(
                board,
                text,
                schematicBomRows,
                respectComponentVisibility: !IsDesignatorHelperLayer(board, text.Layer));
            if (drawing is not null)
            {
                AddDrawingToBucket(drawing, text.Layer);
            }
        }

        return new JsonObject
        {
            ["silkscreen"] = new JsonObject
            {
                ["F"] = frontSilkscreen,
                ["B"] = backSilkscreen,
            },
            ["fabrication"] = new JsonObject
            {
                ["F"] = frontFabrication,
                ["B"] = backFabrication,
            },
        };

        void AddDrawingToBucket(JsonObject drawing, int layer)
        {
            switch (GetDrawingBucket(board, layer))
            {
                case ("silkscreen", "F"):
                    frontSilkscreen.Add(drawing);
                    break;
                case ("silkscreen", "B"):
                    backSilkscreen.Add(drawing);
                    break;
                case ("fabrication", "F"):
                    frontFabrication.Add(drawing);
                    break;
                case ("fabrication", "B"):
                    backFabrication.Add(drawing);
                    break;
            }
        }
    }

    private static JsonObject CreateTrackDrawing(PcbTrack track) =>
        new()
        {
            ["type"] = "segment",
            ["start"] = ToIbomPoint(track.Start),
            ["end"] = ToIbomPoint(track.End),
            ["width"] = Round(Math.Max(track.Width.ToMils(), 0.1)),
        };

    private static JsonObject CreateArcDrawing(PcbArc arc)
    {
        if (IsFullCircleArc(arc))
        {
            return new JsonObject
            {
                ["type"] = "circle",
                ["start"] = ToIbomPoint(arc.Center),
                ["radius"] = Round(arc.Radius.ToMils()),
                ["width"] = Round(Math.Max(arc.Width.ToMils(), 0.1)),
            };
        }

        return new JsonObject
        {
            ["type"] = "arc",
            ["start"] = ToIbomPoint(arc.Center),
            ["radius"] = Round(arc.Radius.ToMils()),
            ["startangle"] = AngleForIbom(arc.EndAngle),
            ["endangle"] = AngleForIbom(arc.StartAngle),
            ["width"] = Round(Math.Max(arc.Width.ToMils(), 0.1)),
        };
    }

    private static JsonObject CreateFillDrawing(PcbFill fill)
    {
        var minX = Math.Min(fill.Corner1.X.ToMils(), fill.Corner2.X.ToMils());
        var maxX = Math.Max(fill.Corner1.X.ToMils(), fill.Corner2.X.ToMils());
        var minY = Math.Min(fill.Corner1.Y.ToMils(), fill.Corner2.Y.ToMils());
        var maxY = Math.Max(fill.Corner1.Y.ToMils(), fill.Corner2.Y.ToMils());

        return new JsonObject
        {
            ["type"] = "polygon",
            ["filled"] = 1,
            ["pos"] = new JsonArray(0, 0),
            ["angle"] = 0,
            ["polygons"] = new JsonArray
            {
                new JsonArray
                {
                    ToIbomPoint(new CoordPoint(Coord.FromMils(minX), Coord.FromMils(minY))),
                    ToIbomPoint(new CoordPoint(Coord.FromMils(maxX), Coord.FromMils(minY))),
                    ToIbomPoint(new CoordPoint(Coord.FromMils(maxX), Coord.FromMils(maxY))),
                    ToIbomPoint(new CoordPoint(Coord.FromMils(minX), Coord.FromMils(maxY))),
                },
            },
        };
    }

    private static JsonObject? CreateRegionDrawing(PcbRegion region)
    {
        if (region.Outline.Count < 3)
        {
            return null;
        }

        var polygon = new JsonArray();
        foreach (var point in region.Outline)
        {
            polygon.Add(ToIbomPoint(point));
        }

        return new JsonObject
        {
            ["type"] = "polygon",
            ["filled"] = 1,
            ["pos"] = new JsonArray(0, 0),
            ["angle"] = 0,
            ["polygons"] = new JsonArray { polygon },
        };
    }

    private static JsonObject? CreateTextDrawing(
        PcbDocument board,
        PcbText text,
        IReadOnlyDictionary<string, AltiumBomRow>? schematicBomRows,
        bool respectComponentVisibility)
    {
        var component = GetTextComponent(board, text);
        if (schematicBomRows is not null && component is not null)
        {
            var designator = GetComponentReference(component, text.ComponentIndex);
            if (!schematicBomRows.ContainsKey(designator))
            {
                return null;
            }
        }

        if (respectComponentVisibility && !IsVisibleComponentText(board, text))
        {
            return null;
        }

        var content = ResolveTextContent(board, text, schematicBomRows);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var geometry = AltiumPcbTextGeometryRenderer.Render(text, content);
        if (geometry is null)
        {
            return null;
        }

        var result = new JsonObject
        {
            ["svgpath"] = geometry.SvgPath,
        };
        if (geometry.Thickness is not null)
        {
            result["thickness"] = geometry.Thickness.Value;
        }
        else
        {
            result["fillrule"] = "evenodd";
        }

        if (IsDesignatorText(text))
        {
            result["ref"] = 1;
        }

        if (IsCommentText(text))
        {
            result["val"] = 1;
        }

        return result;
    }

    private static string ResolveTextContent(
        PcbDocument board,
        PcbText text,
        IReadOnlyDictionary<string, AltiumBomRow>? schematicBomRows)
    {
        var content = text.Text ?? "";
        if (content.Length == 0)
        {
            return "";
        }

        var component = GetTextComponent(board, text);
        if (component is null)
        {
            return content;
        }

        AltiumBomRow? bomRow = null;
        if (schematicBomRows is not null)
        {
            var designator = GetComponentReference(component, text.ComponentIndex);
            schematicBomRows.TryGetValue(designator, out bomRow);
        }

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Designator"] = bomRow?.Designator ?? GetComponentReference(component, text.ComponentIndex),
            ["Value"] = bomRow?.Value ?? FirstNotEmpty(component.Comment, component.Description),
            ["Comment"] = bomRow?.Parameters.TryGetValue("Comment", out var rowComment) == true
                ? rowComment
                : bomRow?.Value ?? FirstNotEmpty(component.Comment, component.Description),
            ["Description"] = bomRow?.Description ?? component.Description ?? "",
            ["CurrentFootprint"] = FirstNotEmpty(bomRow?.Footprint, GetComponentFootprint(component)),
            ["Footprint"] = FirstNotEmpty(bomRow?.Footprint, GetComponentFootprint(component)),
        };

        if (bomRow is not null)
        {
            foreach (var (key, value) in bomRow.ProjectParameters)
            {
                lookup[key] = value;
            }

            foreach (var (key, value) in bomRow.Parameters)
            {
                lookup[key] = value;
            }
        }

        if (component.AdditionalParameters is not null)
        {
            foreach (var (key, value) in component.AdditionalParameters)
            {
                lookup.TryAdd(key, value);
            }
        }

        return MacroRegex().Replace(content, match =>
        {
            var name = match.Groups[1].Value;
            return lookup.TryGetValue(name, out var value)
                ? ResolveSimpleAltiumExpression(value, lookup)
                : match.Value;
        }).Trim();
    }

    private static bool IsVisibleComponentText(PcbDocument board, PcbText text)
    {
        var component = GetTextComponent(board, text);
        if (component is null)
        {
            return true;
        }

        if (IsDesignatorText(text))
        {
            return component.NameOn;
        }

        if (IsCommentText(text))
        {
            return component.CommentOn;
        }

        return true;
    }

    private static PcbComponent? GetTextComponent(PcbDocument board, PcbText text)
    {
        var components = board.Components.OfType<PcbComponent>().ToArray();
        return text.ComponentIndex >= 0 && text.ComponentIndex < components.Length
            ? components[text.ComponentIndex]
            : null;
    }

    private static bool IsDesignatorText(PcbText text) =>
        text.IsDesignator ||
        string.Equals((text.Text ?? "").Trim(), ".Designator", StringComparison.OrdinalIgnoreCase);

    private static bool IsCommentText(PcbText text)
    {
        var value = (text.Text ?? "").Trim();
        return text.IsComment || value.StartsWith(".Comment", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject CreateZones(PcbDocument board)
    {
        var front = new JsonArray();
        var back = new JsonArray();

        if (board.ShapeBasedRegions.Count > 0)
        {
            foreach (var region in board.ShapeBasedRegions)
            {
                if (!RegionIsCopper(region))
                {
                    continue;
                }

                var zone = CreateRegionZone(board, region);
                if (zone is null)
                {
                    continue;
                }

                if (IsTop(region.Layer))
                {
                    front.Add(zone);
                }
                else if (IsBottom(region.Layer))
                {
                    back.Add(zone);
                }
            }
        }
        else
        {
            foreach (var region in board.Regions.OfType<PcbRegion>())
            {
                if (!RegionIsCopper(region))
                {
                    continue;
                }

                var zone = CreateRegionZone(board, region);
                if (zone is null)
                {
                    continue;
                }

                if (IsTop(region.Layer))
                {
                    front.Add(zone);
                }
                else if (IsBottom(region.Layer))
                {
                    back.Add(zone);
                }
            }
        }

        return new JsonObject
        {
            ["F"] = front,
            ["B"] = back,
        };
    }

    private static JsonObject? CreateRegionZone(PcbDocument board, PcbShapeBasedRegion region)
    {
        var polygons = CreateRegionPolygons(region);
        if (polygons.Count == 0)
        {
            return null;
        }

        var zone = new JsonObject
        {
            ["polygons"] = polygons,
            ["fillrule"] = "evenodd",
        };

        var net = GetNetName(board, region.NetIndex, null);
        if (!string.IsNullOrWhiteSpace(net))
        {
            zone["net"] = net;
        }

        return zone;
    }

    private static JsonObject? CreateRegionZone(PcbDocument board, PcbRegion region)
    {
        var polygons = CreateRegionPolygons(region);
        if (polygons.Count == 0)
        {
            return null;
        }

        var zone = new JsonObject
        {
            ["polygons"] = polygons,
            ["fillrule"] = "evenodd",
        };

        var net = GetNetName(board, region.NetIndex, region.Net);
        if (!string.IsNullOrWhiteSpace(net))
        {
            zone["net"] = net;
        }

        return zone;
    }

    private static bool RegionIsCopper(PcbRegion region) =>
        region.Kind == 0;

    private static bool RegionIsCopper(PcbShapeBasedRegion region)
    {
        var kind = region.GetProperty("KIND");
        if (string.IsNullOrWhiteSpace(kind))
        {
            return true;
        }

        if (int.TryParse(kind, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value == 0;
        }

        return kind.EndsWith("COPPER", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonArray CreateFootprintCopperDrawings(PcbComponent component)
    {
        var drawings = new JsonArray();

        foreach (var track in component.Tracks.OfType<PcbTrack>())
        {
            var side = CopperSide(track.Layer);
            if (side is not null)
            {
                drawings.Add(new JsonObject
                {
                    ["layer"] = side,
                    ["drawing"] = CreateTrackDrawing(track),
                });
            }
        }

        foreach (var arc in component.Arcs.OfType<PcbArc>())
        {
            var side = CopperSide(arc.Layer);
            if (side is not null)
            {
                drawings.Add(new JsonObject
                {
                    ["layer"] = side,
                    ["drawing"] = CreateArcDrawing(arc),
                });
            }
        }

        foreach (var region in component.Regions.OfType<PcbRegion>())
        {
            var side = CopperSide(region.Layer);
            var drawing = CreateRegionDrawing(region);
            if (side is not null && drawing is not null)
            {
                drawings.Add(new JsonObject
                {
                    ["layer"] = side,
                    ["drawing"] = drawing,
                });
            }
        }

        return drawings;
    }

    private static JsonObject CreateFootprintBoundingBox(PcbDocument board, PcbComponent component, JsonArray fallbackCenter)
    {
        Bounds? outlineBounds = null;
        foreach (var track in component.Tracks.OfType<PcbTrack>())
        {
            if (IsComponentOutlineLayer(board, track.Layer))
            {
                outlineBounds = ExtendBounds(outlineBounds, TrackBounds(track));
            }
        }

        foreach (var arc in component.Arcs.OfType<PcbArc>())
        {
            if (IsComponentOutlineLayer(board, arc.Layer))
            {
                outlineBounds = ExtendBounds(outlineBounds, ArcBounds(arc));
            }
        }

        foreach (var fill in component.Fills.OfType<PcbFill>())
        {
            if (IsComponentOutlineLayer(board, fill.Layer))
            {
                outlineBounds = ExtendBounds(outlineBounds, FillBounds(fill));
            }
        }

        foreach (var region in component.Regions.OfType<PcbRegion>())
        {
            if (!IsComponentOutlineLayer(board, region.Layer))
            {
                continue;
            }

            var regionBounds = RegionBounds(region);
            if (regionBounds is not null)
            {
                outlineBounds = ExtendBounds(outlineBounds, regionBounds.Value);
            }
        }

        if (outlineBounds is not null)
        {
            return BoundsToBoundingBox(outlineBounds.Value);
        }

        Bounds? padBounds = null;
        foreach (var pad in component.Pads.OfType<PcbPad>())
        {
            padBounds = ExtendBounds(padBounds, PadBounds(pad));
        }

        if (padBounds is not null)
        {
            return BoundsToBoundingBox(padBounds.Value);
        }

        return new JsonObject
        {
            ["pos"] = fallbackCenter.DeepClone(),
            ["relpos"] = new JsonArray(0, 0),
            ["size"] = new JsonArray(1, 1),
            ["angle"] = 0,
        };
    }

    private static JsonObject BoundsToBoundingBox(Bounds bounds) =>
        new()
        {
            ["pos"] = new JsonArray(Round(bounds.MinX), Round(bounds.MinY)),
            ["relpos"] = new JsonArray(0, 0),
            ["size"] = new JsonArray(
                Round(Math.Max(bounds.MaxX - bounds.MinX, 1)),
                Round(Math.Max(bounds.MaxY - bounds.MinY, 1))),
            ["angle"] = 0,
        };

    private static Bounds TrackBounds(PcbTrack track)
    {
        var start = ToIbomTuple(track.Start);
        var end = ToIbomTuple(track.End);
        var margin = track.Width.ToMils() / 2.0;
        return new Bounds(
            Math.Min(start.X, end.X) - margin,
            Math.Min(start.Y, end.Y) - margin,
            Math.Max(start.X, end.X) + margin,
            Math.Max(start.Y, end.Y) + margin);
    }

    private static Bounds ArcBounds(PcbArc arc)
    {
        var center = ToIbomTuple(arc.Center);
        var extent = arc.Radius.ToMils() + arc.Width.ToMils() / 2.0;
        return new Bounds(center.X - extent, center.Y - extent, center.X + extent, center.Y + extent);
    }

    private static Bounds FillBounds(PcbFill fill)
    {
        var first = ToIbomTuple(fill.Corner1);
        var second = ToIbomTuple(fill.Corner2);
        return new Bounds(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Max(first.X, second.X),
            Math.Max(first.Y, second.Y));
    }

    private static Bounds? RegionBounds(PcbRegion region)
    {
        var points = CreateRegionPoints(region);
        return points.Count == 0 ? null : BoundsFromPoints(points);
    }

    private static Bounds PadBounds(PcbPad pad)
    {
        var center = ToIbomTuple(pad.Location);
        var size = GetPadSize(pad);
        var angle = -AngleForIbom(pad.Rotation) * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var points = new List<(double X, double Y)>(4);
        foreach (var (localX, localY) in new[]
                 {
                     (-size.X / 2, -size.Y / 2),
                     (size.X / 2, -size.Y / 2),
                     (size.X / 2, size.Y / 2),
                     (-size.X / 2, size.Y / 2),
                 })
        {
            points.Add((
                center.X + localX * cos - localY * sin,
                center.Y + localX * sin + localY * cos));
        }

        return BoundsFromPoints(points);
    }

    private static JsonArray CreateRegionPolygons(PcbRegion region)
    {
        var polygon = new JsonArray();
        foreach (var point in region.Outline)
        {
            polygon.Add(ToIbomPoint(point));
        }

        return polygon.Count >= 3
            ? new JsonArray { polygon }
            : [];
    }

    private static JsonArray CreateRegionPolygons(PcbShapeBasedRegion region)
    {
        var polygons = new JsonArray();

        var outline = ToShapeBasedPolygon(region.Outline.Select(v => ((double)v.X, (double)v.Y)));
        if (outline.Count >= 3)
        {
            polygons.Add(outline);
        }

        foreach (var hole in region.Holes)
        {
            var polygon = ToShapeBasedPolygon(hole);
            if (polygon.Count >= 3)
            {
                polygons.Add(polygon);
            }
        }

        return polygons;
    }

    private static JsonArray ToShapeBasedPolygon(IEnumerable<(double X, double Y)> rawPoints)
    {
        var points = rawPoints.ToList();
        if (points.Count >= 2 && points[0].X == points[^1].X && points[0].Y == points[^1].Y)
        {
            points.RemoveAt(points.Count - 1);
        }

        var polygon = new JsonArray();
        foreach (var point in points)
        {
            polygon.Add(ToIbomPointRaw(point.X, point.Y));
        }

        return polygon;
    }

    private static List<(double X, double Y)> CreateRegionPoints(PcbRegion region) =>
        region.Outline
            .Select(ToIbomTuple)
            .ToList();

    private static JsonArray CreateBoardEdges(PcbDocument board, List<(double X, double Y)> allPoints)
    {
        var edges = new JsonArray();
        var outline = board.GetBoardOutline();
        if (outline.Count >= 2)
        {
            for (var i = 0; i < outline.Count; i++)
            {
                var start = ToIbomPoint(outline[i]);
                var end = ToIbomPoint(outline[(i + 1) % outline.Count]);
                edges.Add(new JsonObject
                {
                    ["type"] = "segment",
                    ["start"] = start,
                    ["end"] = end,
                    ["width"] = 1,
                });

                allPoints.Add((start[0]!.GetValue<double>(), start[1]!.GetValue<double>()));
                allPoints.Add((end[0]!.GetValue<double>(), end[1]!.GetValue<double>()));
            }
        }

        if (edges.Count == 0)
        {
            var bounds = BoundsFromPoints(allPoints);
            edges.Add(new JsonObject
            {
                ["type"] = "rect",
                ["start"] = new JsonArray(Round(bounds.MinX), Round(bounds.MinY)),
                ["end"] = new JsonArray(Round(bounds.MaxX), Round(bounds.MaxY)),
                ["width"] = 1,
            });
        }

        return edges;
    }

    private static JsonObject CreateBoundingBox(PcbComponent component, JsonArray center)
    {
        var bounds = component.Bounds;
        if (!bounds.IsEmpty)
        {
            var min = ToIbomPoint(bounds.Min);
            var max = ToIbomPoint(bounds.Max);
            var minX = Math.Min(min[0]!.GetValue<double>(), max[0]!.GetValue<double>());
            var maxX = Math.Max(min[0]!.GetValue<double>(), max[0]!.GetValue<double>());
            var minY = Math.Min(min[1]!.GetValue<double>(), max[1]!.GetValue<double>());
            var maxY = Math.Max(min[1]!.GetValue<double>(), max[1]!.GetValue<double>());
            return new JsonObject
            {
                ["pos"] = new JsonArray(Round(minX), Round(minY)),
                ["relpos"] = new JsonArray(0, 0),
                ["size"] = new JsonArray(Round(Math.Max(maxX - minX, 1)), Round(Math.Max(maxY - minY, 1))),
                ["angle"] = 0,
            };
        }

        return new JsonObject
        {
            ["pos"] = center.DeepClone(),
            ["relpos"] = new JsonArray(0, 0),
            ["size"] = new JsonArray(1, 1),
            ["angle"] = 0,
        };
    }

    private static AltiumBomRow CreateBomRow(
        PcbComponent component,
        int index,
        IReadOnlyDictionary<string, AltiumBomRow>? schematicBomRows)
    {
        var designator = GetComponentReference(component, index);
        var footprint = GetComponentFootprint(component);
        if (schematicBomRows is not null && schematicBomRows.TryGetValue(designator, out var schematicRow))
        {
            var parameters = new Dictionary<string, string>(schematicRow.Parameters, StringComparer.OrdinalIgnoreCase);
            AddIfNotEmpty(parameters, "CurrentFootprint", footprint);
            AddIfNotEmpty(parameters, "Footprint", FirstNotEmpty(footprint, schematicRow.Footprint));
            return schematicRow with
            {
                Footprint = FirstNotEmpty(footprint, schematicRow.Footprint),
                Parameters = parameters,
            };
        }

        var description = component.Description ?? component.SourceDescription ?? "";
        var value = FirstNotEmpty(
            component.Comment,
            GetParameter(component, "COMMENT"),
            description);
        var fallbackParameters = CreateBomParameters(component, designator, value, description, footprint);

        return new AltiumBomRow(designator, value, footprint, description, fallbackParameters);
    }

    private static Dictionary<string, string> CreateBomParameters(
        PcbComponent component,
        string designator,
        string value,
        string description,
        string footprint)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfNotEmpty(parameters, "Designator", designator);
        AddIfNotEmpty(parameters, "Comment", value);
        AddIfNotEmpty(parameters, "Value", value);
        AddIfNotEmpty(parameters, "Description", description);
        AddIfNotEmpty(parameters, "CurrentFootprint", footprint);
        AddIfNotEmpty(parameters, "Footprint", footprint);
        AddIfNotEmpty(parameters, "SourceDesignator", component.SourceDesignator);
        AddIfNotEmpty(parameters, "SourceLibrary", component.SourceComponentLibrary);
        AddIfNotEmpty(parameters, "SourceFootprintLibrary", component.SourceFootprintLibrary);
        AddIfNotEmpty(parameters, "SourceLibraryReference", component.SourceLibReference);

        if (component.AdditionalParameters is not null)
        {
            foreach (var (key, parameterValue) in component.AdditionalParameters)
            {
                AddIfNotEmpty(parameters, key, parameterValue);
            }
        }

        return parameters;
    }

    private static Dictionary<string, string> CreateComponentLookup(AltiumBomRow row)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in row.ProjectParameters)
        {
            lookup[key] = value;
        }

        foreach (var (key, value) in row.Parameters)
        {
            lookup[key] = value;
        }

        lookup["designator"] = row.Designator;
        lookup["value"] = row.Value;
        lookup["description"] = row.Description;
        lookup["currentfootprint"] = row.Footprint;
        lookup["footprint"] = row.Footprint;
        lookup.TryAdd("comment", row.Value);
        return lookup;
    }

    private static string ResolveComponentValue(AltiumBomRow row, Dictionary<string, string> lookup)
    {
        if (!row.Parameters.TryGetValue("Comment", out var comment) || string.IsNullOrWhiteSpace(comment))
        {
            return ResolveSimpleAltiumExpression(row.Value, lookup);
        }

        if (!comment.StartsWith('='))
        {
            return comment;
        }

        return ResolveSimpleAltiumExpression(comment, lookup, fallback: row.Value);
    }

    private static JsonObject ResolveExtraFields(
        IReadOnlyDictionary<string, string> parameters,
        Dictionary<string, string> lookup)
    {
        var fields = new JsonObject();
        foreach (var (key, value) in parameters)
        {
            var resolved = ResolveSimpleAltiumExpression(value, lookup);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                fields[key] = resolved;
            }
        }

        return fields;
    }

    private static string ResolveSimpleAltiumExpression(
        string? value,
        Dictionary<string, string> lookup,
        int depth = 0,
        string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(value) || depth > 4)
        {
            return value ?? "";
        }

        var stripped = value.Trim();
        var isExpression = stripped.StartsWith('=');
        if ((isExpression || stripped.StartsWith('.')) && stripped.Length > 1)
        {
            var name = stripped[1..].Trim();
            if (isExpression && (name.Contains('+', StringComparison.Ordinal) || name.Contains('\'', StringComparison.Ordinal)))
            {
                return EvaluateAltiumExpression(name, lookup);
            }

            if (lookup.TryGetValue(name, out var resolved))
            {
                return ResolveSimpleAltiumExpression(resolved, lookup, depth + 1);
            }

            return fallback ?? value;
        }

        return value;
    }

    private static string EvaluateAltiumExpression(string expression, Dictionary<string, string> lookup)
    {
        var parts = new List<string>();
        for (var i = 0; i < expression.Length;)
        {
            var ch = expression[i];
            if (char.IsWhiteSpace(ch) || ch == '+')
            {
                i++;
                continue;
            }

            if (ch == '\'')
            {
                var end = expression.IndexOf('\'', i + 1);
                if (end < 0)
                {
                    parts.Add(expression[(i + 1)..]);
                    break;
                }

                parts.Add(expression[(i + 1)..end]);
                i = end + 1;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                i++;
                while (i < expression.Length && (char.IsLetterOrDigit(expression[i]) || expression[i] == '_'))
                {
                    i++;
                }

                var name = expression[start..i];
                parts.Add(lookup.TryGetValue(name, out var value) ? value : "");
                continue;
            }

            i++;
        }

        return string.Concat(parts);
    }

    private static void AddIfNotEmpty(Dictionary<string, string> fields, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.TryAdd(key, value);
        }
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static string? GetParameter(PcbComponent component, string name) =>
        component.AdditionalParameters is not null &&
        component.AdditionalParameters.TryGetValue(name, out var value)
            ? value
            : null;

    private static JsonArray CreatePadLayers(PcbPad pad)
    {
        if (pad.HoleSize.ToRaw() > 0 || pad.Layer == 74)
        {
            return new JsonArray("F", "B");
        }

        return new JsonArray(IsBottom(pad.Layer) ? "B" : "F");
    }

    private static (double X, double Y) GetPadSize(PcbPad pad)
    {
        var size = IsBottom(pad.Layer) ? pad.SizeBottom : pad.SizeTop;
        if (size.X.ToRaw() == 0 || size.Y.ToRaw() == 0)
        {
            size = pad.SizeTop.X.ToRaw() != 0 && pad.SizeTop.Y.ToRaw() != 0 ? pad.SizeTop : pad.SizeMiddle;
        }

        return (Math.Max(size.X.ToMils(), 1), Math.Max(size.Y.ToMils(), 1));
    }

    private static string GetPadShape(PcbPad pad)
    {
        var shape = IsBottom(pad.Layer) ? pad.ShapeBottom : pad.ShapeTop;
        return shape switch
        {
            PadShape.Rectangular => "rect",
            PadShape.RoundedRectangle => "roundrect",
            PadShape.Octagonal => "chamfrect",
            _ => Math.Abs(GetPadSize(pad).X - GetPadSize(pad).Y) < 0.001 ? "circle" : "oval",
        };
    }

    private static string GetComponentReference(PcbComponent component, int index) =>
        !string.IsNullOrWhiteSpace(component.SourceDesignator)
            ? component.SourceDesignator!
            : !string.IsNullOrWhiteSpace(component.AdditionalParameters?.GetValueOrDefault("DESIGNATOR"))
                ? component.AdditionalParameters!["DESIGNATOR"]
                : $"#{index + 1}";

    private static string GetComponentFootprint(PcbComponent component) =>
        !string.IsNullOrWhiteSpace(component.Pattern)
            ? component.Pattern!
            : !string.IsNullOrWhiteSpace(component.Name)
                ? component.Name
                : component.FootprintDescription ?? "";

    private static JsonArray ToIbomPoint(CoordPoint point) =>
        new(Round(point.X.ToMils()), Round(-point.Y.ToMils()));

    private static JsonArray ToIbomPointRaw(double xRaw, double yRaw) =>
        new(Round(xRaw / 10000.0), Round(-yRaw / 10000.0));

    private static (double X, double Y) ToIbomTuple(CoordPoint point) =>
        (Round(point.X.ToMils()), Round(-point.Y.ToMils()));

    private static double AngleForIbom(double angle) => Round((-angle % 360 + 360) % 360);

    private static (double Start, double End) ArcAnglesForIbom(double startAngle, double endAngle) =>
        (AngleForIbom(endAngle), AngleForIbom(startAngle));

    private static bool IsFullCircleArc(PcbArc arc) =>
        Math.Abs(arc.EndAngle - arc.StartAngle) >= 359.999;

    private static bool IsTop(int layer) => layer == 1;

    private static bool IsBottom(int layer) => layer == 32;

    private static string? CopperSide(int layer) =>
        IsTop(layer) ? "F" : IsBottom(layer) ? "B" : null;

    private static (string Kind, string Side)? GetDrawingBucket(PcbDocument board, int layer)
    {
        if (IsTopDesignatorLayer(board, layer))
        {
            return ("silkscreen", "F");
        }

        if (IsBottomDesignatorLayer(board, layer))
        {
            return ("silkscreen", "B");
        }

        var name = GetLayerName(board, layer);
        if (layer == 33)
        {
            return ("silkscreen", "F");
        }

        if (layer == 34)
        {
            return ("silkscreen", "B");
        }

        if (name.Contains("top componentscontours", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("top assembly", StringComparison.OrdinalIgnoreCase))
        {
            return ("fabrication", "F");
        }

        if (name.Contains("bottom componentscontours", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("bottom assembly", StringComparison.OrdinalIgnoreCase))
        {
            return ("fabrication", "B");
        }

        return null;
    }

    private static bool IsDesignatorHelperLayer(PcbDocument board, int layer) =>
        IsTopDesignatorLayer(board, layer) || IsBottomDesignatorLayer(board, layer);

    private static bool IsTopDesignatorLayer(PcbDocument board, int layer) =>
        layer == 65 ||
        GetLayerName(board, layer).Contains("top designator", StringComparison.OrdinalIgnoreCase);

    private static bool IsBottomDesignatorLayer(PcbDocument board, int layer) =>
        layer == 66 ||
        GetLayerName(board, layer).Contains("bottom designator", StringComparison.OrdinalIgnoreCase);

    private static bool IsComponentOutlineLayer(PcbDocument board, int layer)
    {
        var name = GetLayerName(board, layer);
        return name.Contains("componentscontours", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("assembly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEdgeLayer(PcbDocument board, int layer)
    {
        if (layer == 39)
        {
            return true;
        }

        var normalized = NormalizeLayerName(GetLayerName(board, layer));
        return normalized is "pcbcontour" or "boardoutline" or "edgecuts";
    }

    private static string GetLayerName(PcbDocument board, int layer) =>
        board.LayerStack?.Layers.FirstOrDefault(l => l.Index == layer)?.Name ??
        board.GetStackup()?.ForLayer(layer)?.Name ??
        GetFallbackLayerName(layer);

    private static string GetFallbackLayerName(int layer) =>
        layer switch
        {
            1 => "Top Layer",
            32 => "Bottom Layer",
            33 => "Top Overlay",
            34 => "Bottom Overlay",
            35 => "Top Paste",
            36 => "Bottom Paste",
            37 => "Top Solder",
            38 => "Bottom Solder",
            39 => "Keepout",
            >= 57 and <= 72 => $"Mechanical {layer - 56}",
            _ => "",
        };

    private static string NormalizeLayerName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? GetNetName(PcbDocument board, ushort netIndex, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return netIndex < board.Nets.Count ? board.Nets[netIndex].Name : null;
    }

    private static Bounds ExtendBounds(Bounds? existing, Bounds next) =>
        existing is null
            ? next
            : new Bounds(
                Math.Min(existing.Value.MinX, next.MinX),
                Math.Min(existing.Value.MinY, next.MinY),
                Math.Max(existing.Value.MaxX, next.MaxX),
                Math.Max(existing.Value.MaxY, next.MaxY));

    private static Bounds BoundsFromPoints(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
        {
            return new Bounds(-1000, -1000, 1000, 1000);
        }

        return new Bounds(
            points.Min(p => p.X),
            points.Min(p => p.Y),
            points.Max(p => p.X),
            points.Max(p => p.Y));
    }

    private static Bounds? BoundsFromEdges(JsonArray edges)
    {
        var points = new List<(double X, double Y)>();
        foreach (var edgeNode in edges.OfType<JsonObject>())
        {
            AddPoint(edgeNode["start"]);
            AddPoint(edgeNode["end"]);
        }

        return points.Count == 0 ? null : BoundsFromPoints(points);

        void AddPoint(JsonNode? node)
        {
            if (node is not JsonArray point || point.Count < 2)
            {
                return;
            }

            points.Add((point[0]!.GetValue<double>(), point[1]!.GetValue<double>()));
        }
    }

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\.([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex MacroRegex();

    private readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY);
}

public sealed record AltiumBomRow(
    string Designator,
    string Value,
    string Footprint,
    string Description,
    IReadOnlyDictionary<string, string> Parameters)
{
    public IReadOnlyDictionary<string, string> ProjectParameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
