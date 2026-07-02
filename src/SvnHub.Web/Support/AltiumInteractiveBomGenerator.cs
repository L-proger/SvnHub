using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Eda.Primitives;

namespace SvnHub.Web.Support;

public sealed class AltiumInteractiveBomGenerator
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

            var bbox = CreateBoundingBox(component, center);
            footprints.Add(new JsonObject
            {
                ["ref"] = bomRow.Designator,
                ["center"] = center,
                ["bbox"] = bbox,
                ["pads"] = pads,
                ["drawings"] = new JsonArray(),
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
            ["drawings"] = new JsonObject
            {
                ["silkscreen"] = new JsonObject
                {
                    ["F"] = new JsonArray(),
                    ["B"] = new JsonArray(),
                },
                ["fabrication"] = new JsonObject
                {
                    ["F"] = new JsonArray(),
                    ["B"] = new JsonArray(),
                },
            },
            ["footprints"] = footprints,
            ["tracks"] = CreateTracks(board),
            ["zones"] = new JsonObject
            {
                ["F"] = new JsonArray(),
                ["B"] = new JsonArray(),
            },
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
            var item = new JsonObject
            {
                ["start"] = ToIbomPoint(track.Start),
                ["end"] = ToIbomPoint(track.End),
                ["width"] = Round(track.Width.ToMils()),
            };

            var net = GetNetName(board, track.NetIndex, track.Net);
            if (!string.IsNullOrWhiteSpace(net))
            {
                item["net"] = net;
            }

            if (IsBottom(track.Layer))
            {
                back.Add(item);
            }
            else if (IsTop(track.Layer))
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

        return new JsonObject
        {
            ["F"] = front,
            ["B"] = back,
        };
    }

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

    private static double AngleForIbom(double angle) => Round((-angle % 360 + 360) % 360);

    private static bool IsTop(int layer) => layer == 1;

    private static bool IsBottom(int layer) => layer == 32;

    private static string? GetNetName(PcbDocument board, ushort netIndex, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return netIndex < board.Nets.Count ? board.Nets[netIndex].Name : null;
    }

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
