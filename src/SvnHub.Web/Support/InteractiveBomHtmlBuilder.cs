using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Globalization;
using Microsoft.AspNetCore.Hosting;

namespace SvnHub.Web.Support;

public sealed partial class InteractiveBomHtmlBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private const string InteractiveBomVersion = "v2.11.1";

    private static readonly string[] DefaultShowFields = ["Value", "Footprint"];
    private static readonly string[] DefaultGroupFields = ["Value", "Footprint"];
    private static readonly string[] DefaultComponentSortOrder = ["C", "R", "L", "D", "U", "Y", "X", "F", "SW", "A", "~", "HS", "CNN", "J", "P", "NT", "MH"];

    private readonly IWebHostEnvironment _environment;

    public InteractiveBomHtmlBuilder(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public string Build(string genericJson)
    {
        var root = JsonNode.Parse(genericJson)?.AsObject()
            ?? throw new InvalidOperationException("Interactive BOM JSON root must be an object.");

        if (root["spec_version"]?.GetValue<int>() != 1)
        {
            throw new InvalidOperationException("Unsupported Interactive BOM JSON spec_version.");
        }

        var pcbdata = root["pcbdata"]?.DeepClone()?.AsObject()
            ?? throw new InvalidOperationException("Interactive BOM JSON must contain pcbdata.");

        var components = root["components"]?.AsArray()
            ?? throw new InvalidOperationException("Interactive BOM JSON must contain components.");

        var footprints = pcbdata["footprints"]?.AsArray()
            ?? throw new InvalidOperationException("Interactive BOM pcbdata must contain footprints.");

        if (footprints.Count != components.Count)
        {
            throw new InvalidOperationException("Interactive BOM component count does not match footprint count.");
        }

        NormalizePcbData(pcbdata);

        var config = InteractiveBomConfig.Default;
        pcbdata["bom"] = GenerateBom(components, config);
        pcbdata["ibom_version"] = InteractiveBomVersion;

        var pcbdataJson = pcbdata.ToJsonString(JsonOptions);
        var pcbdataJs = "var pcbdata = JSON.parse(" + JsonSerializer.Serialize(pcbdataJson, JsonOptions) + ")";
        var configJs = "var config = " + JsonSerializer.Serialize(CreateHtmlConfig(config), JsonOptions);

        var html = ReadAsset("ibom.html")
            .Replace("///CSS///", ReadAsset("ibom.css"), StringComparison.Ordinal)
            .Replace("///USERCSS///", "", StringComparison.Ordinal)
            .Replace("///SPLITJS///", ReadAsset("split.js"), StringComparison.Ordinal)
            .Replace("///LZ-STRING///", ReadAsset("lz-string.js"), StringComparison.Ordinal)
            .Replace("///POINTER_EVENTS_POLYFILL///", ReadAsset("pep.js"), StringComparison.Ordinal)
            .Replace("///CONFIG///", CreateErrorOverlayScript() + Environment.NewLine + configJs, StringComparison.Ordinal)
            .Replace("///PCBDATA///", pcbdataJs, StringComparison.Ordinal)
            .Replace("///UTILJS///", ReadAsset("util.js"), StringComparison.Ordinal)
            .Replace("///RENDERJS///", ReadAsset("render.js"), StringComparison.Ordinal)
            .Replace("///TABLEUTILJS///", ReadAsset("table-util.js"), StringComparison.Ordinal)
            .Replace("///IBOMJS///", ReadAsset("ibom.js"), StringComparison.Ordinal)
            .Replace("///USERJS///", "", StringComparison.Ordinal)
            .Replace("///USERHEADER///", CreateLoadingHeader(), StringComparison.Ordinal)
            .Replace("///USERFOOTER///", "", StringComparison.Ordinal);

        return html;
    }

    private static void NormalizePcbData(JsonObject pcbdata)
    {
        CleanTextDrawingControlCharacters(pcbdata);

        if (pcbdata["font_data"] is not JsonObject)
        {
            RemoveTextDrawingsWithoutVectorData(pcbdata);
        }
    }

    private static void CleanTextDrawingControlCharacters(JsonObject pcbdata)
    {
        VisitDrawingObjects(pcbdata, drawing =>
        {
            if (drawing["text"] is JsonValue textValue && textValue.TryGetValue<string>(out var text))
            {
                var clean = new string(text.Where(c => char.GetUnicodeCategory(c) is not UnicodeCategory.Control).ToArray());
                if (!string.Equals(text, clean, StringComparison.Ordinal))
                {
                    drawing["text"] = clean;
                }
            }
        });
    }

    private static void RemoveTextDrawingsWithoutVectorData(JsonObject pcbdata)
    {
        VisitDrawingArrays(pcbdata, RemoveUnsupportedTextDrawings);
    }

    private static void VisitDrawingObjects(JsonObject pcbdata, Action<JsonObject> visit)
    {
        VisitDrawingArrays(pcbdata, drawings =>
        {
            foreach (var item in drawings)
            {
                var drawing = TryGetDrawingObject(item);
                if (drawing is not null)
                {
                    visit(drawing);
                }
            }
        });
    }

    private static void VisitDrawingArrays(JsonObject pcbdata, Action<JsonArray> visit)
    {
        if (pcbdata["drawings"] is JsonObject drawingsByKind)
        {
            foreach (var kind in drawingsByKind)
            {
                if (kind.Value is not JsonObject drawingsBySide)
                {
                    continue;
                }

                foreach (var side in drawingsBySide)
                {
                    if (side.Value is JsonArray drawings)
                    {
                        visit(drawings);
                    }
                }
            }
        }

        if (pcbdata["footprints"] is not JsonArray footprints)
        {
            return;
        }

        foreach (var footprintNode in footprints)
        {
            if (footprintNode is JsonObject footprint && footprint["drawings"] is JsonArray drawings)
            {
                visit(drawings);
            }
        }
    }

    private static void RemoveUnsupportedTextDrawings(JsonArray drawings)
    {
        for (var i = drawings.Count - 1; i >= 0; i--)
        {
            var drawing = TryGetDrawingObject(drawings[i]);
            if (drawing is not null && IsTextDrawingWithoutVectorData(drawing))
            {
                drawings.RemoveAt(i);
            }
        }
    }

    private static JsonObject? TryGetDrawingObject(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        return obj["drawing"] is JsonObject nestedDrawing
            ? nestedDrawing
            : obj;
    }

    private static bool IsTextDrawingWithoutVectorData(JsonObject drawing) =>
        drawing.ContainsKey("text") &&
        !drawing.ContainsKey("svgpath") &&
        !drawing.ContainsKey("polygons");

    private static string CreateErrorOverlayScript() =>
        """
        (function () {
          function showSvnHubInteractiveBomMessage(message, isError) {
            function render() {
              if (!document.body) {
                window.setTimeout(render, 0);
                return;
              }
              document.body.innerHTML =
                "<pre style=\"white-space:pre-wrap;margin:16px;padding:12px;border:1px solid " +
                (isError ? "#dc3545" : "#6c757d") +
                ";border-radius:6px;color:" +
                (isError ? "#842029" : "#343a40") +
                ";background:" +
                (isError ? "#f8d7da" : "#f8f9fa") +
                ";font:13px/1.45 monospace;\"></pre>";
              document.body.firstChild.textContent = message;
            }
            render();
          }

          window.addEventListener("error", function (event) {
            var message = event.message || "Interactive BOM preview failed.";
            if (event.error && event.error.stack) {
              message += "\n\n" + event.error.stack;
            }
            showSvnHubInteractiveBomMessage(message, true);
          });

          window.addEventListener("unhandledrejection", function (event) {
            var reason = event.reason || "Interactive BOM preview failed.";
            var message = reason && reason.stack ? reason.stack : String(reason);
            showSvnHubInteractiveBomMessage(message, true);
          });

          window.addEventListener("load", function () {
            window.setTimeout(function () {
              var loading = document.getElementById("svnhub-ibom-loading");
              if (!loading) {
                return;
              }

              if (document.getElementById("bombody") && document.getElementById("bombody").children.length > 0) {
                loading.remove();
              } else {
                loading.textContent = "Interactive BOM loaded, but no BOM rows were rendered.";
              }
            }, 0);
          });
        })();
        """;

    private static string CreateLoadingHeader() =>
        """
        <div id="svnhub-ibom-loading" style="margin:16px;padding:12px;border:1px solid #6c757d;border-radius:6px;color:#343a40;background:#f8f9fa;font:13px/1.45 monospace;">
          Loading Interactive BOM preview...
        </div>
        """;

    private static JsonObject GenerateBom(JsonArray components, InteractiveBomConfig config)
    {
        var skipped = new JsonArray();
        var fieldsByIndex = new JsonObject();
        var groups = new Dictionary<string, List<ComponentRef>>(StringComparer.Ordinal);
        var componentInfos = new List<ComponentInfo>(components.Count);
        var fieldsByComponent = new Dictionary<int, JsonArray>();
        var groupBy = new HashSet<string>(config.GroupFields, StringComparer.Ordinal);

        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i]?.AsObject()
                ?? throw new InvalidOperationException("Interactive BOM components must be objects.");

            var info = ComponentInfo.FromJson(i, component);
            componentInfos.Add(info);

            if (SkipComponent(info, config))
            {
                skipped.Add(i);
                continue;
            }

            var fields = new JsonArray();
            var groupKey = new List<string>();
            foreach (var field in config.ShowFields)
            {
                if (string.Equals(field, "Value", StringComparison.Ordinal))
                {
                    fields.Add(info.Value);
                    if (groupBy.Contains("Value"))
                    {
                        var normalized = ComponentValue(info.Value, info.Reference);
                        groupKey.Add(normalized.Value);
                        groupKey.Add(normalized.Unit ?? "");
                    }
                }
                else if (string.Equals(field, "Footprint", StringComparison.Ordinal))
                {
                    fields.Add(info.Footprint);
                    if (groupBy.Contains("Footprint"))
                    {
                        groupKey.Add(info.Footprint);
                        groupKey.Add(info.Attr);
                    }
                }
                else
                {
                    var fieldKey = config.NormalizeFieldCase ? field.ToLowerInvariant() : field;
                    var value = info.ExtraFields.TryGetValue(fieldKey, out var extraValue) ? extraValue : "";
                    fields.Add(value);
                    if (groupBy.Contains(field))
                    {
                        groupKey.Add(value);
                    }
                }
            }

            fieldsByComponent[i] = fields;

            var groupKeyText = string.Join('\u001f', groupKey);
            if (!groups.TryGetValue(groupKeyText, out var refs))
            {
                refs = [];
                groups[groupKeyText] = refs;
            }

            refs.Add(new ComponentRef(info.Reference, i));
        }

        ConvertNumericExtraFieldColumns(fieldsByComponent, config);

        var both = groups.Values
            .Select(refs => refs.OrderBy(r => r.Reference, NaturalReferenceComparer.Instance).ThenBy(r => r.Index).ToArray())
            .ToArray();

        if (groupBy.Contains("Value"))
        {
            FixGroupedDisplayValues(both, fieldsByComponent, config);
        }

        foreach (var (index, fields) in fieldsByComponent)
        {
            fieldsByIndex[index.ToString(CultureInfo.InvariantCulture)] = fields;
        }

        var bothRows = both
            .OrderBy(row => (IReadOnlyCollection<ComponentRef>)row, RowComparer.For(config))
            .Select(ToJsonRow)
            .ToArray();

        return new JsonObject
        {
            ["both"] = ToJsonRows(bothRows),
            ["F"] = ToJsonRows(FilterRows(bothRows, componentInfos, config, "F")),
            ["B"] = ToJsonRows(FilterRows(bothRows, componentInfos, config, "B")),
            ["skipped"] = skipped,
            ["fields"] = fieldsByIndex,
        };
    }

    private static bool SkipComponent(ComponentInfo component, InteractiveBomConfig config)
    {
        var refPrefix = RefLettersPrefixRegex().Match(component.Reference).Value;
        if (config.ComponentBlacklist.Contains(component.Reference, StringComparer.Ordinal) ||
            config.ComponentBlacklist.Contains(refPrefix + "*", StringComparer.Ordinal))
        {
            return true;
        }

        if (config.BlacklistEmptyValue && (component.Value.Length == 0 || component.Value == "~"))
        {
            return true;
        }

        if (config.BlacklistVirtual && string.Equals(component.Attr, "Virtual", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(config.DnpField) &&
            component.ExtraFields.TryGetValue(config.DnpField, out var dnpValue) &&
            !string.IsNullOrWhiteSpace(dnpValue))
        {
            return true;
        }

        const string emptyVariant = "<empty>";
        if (!string.IsNullOrWhiteSpace(config.BoardVariantField) && config.BoardVariantWhitelist.Count != 0)
        {
            var variant = component.ExtraFields.TryGetValue(config.BoardVariantField, out var value) && value.Length != 0
                ? value
                : emptyVariant;

            if (!config.BoardVariantWhitelist.Contains(variant, StringComparer.Ordinal))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.BoardVariantField) && config.BoardVariantBlacklist.Count != 0)
        {
            var variant = component.ExtraFields.TryGetValue(config.BoardVariantField, out var value) && value.Length != 0
                ? value
                : emptyVariant;

            if (variant != emptyVariant && config.BoardVariantBlacklist.Contains(variant, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ConvertNumericExtraFieldColumns(Dictionary<int, JsonArray> fieldsByComponent, InteractiveBomConfig config)
    {
        for (var fieldIndex = 0; fieldIndex < config.ShowFields.Count; fieldIndex++)
        {
            var field = config.ShowFields[fieldIndex];
            if (string.Equals(field, "Value", StringComparison.Ordinal) ||
                string.Equals(field, "Footprint", StringComparison.Ordinal))
            {
                continue;
            }

            var allNumeric = true;
            foreach (var fields in fieldsByComponent.Values)
            {
                var value = fields[fieldIndex]?.ToString() ?? "";
                if (!IsDigitsOnly(value) && value.Trim().Length > 0)
                {
                    allNumeric = false;
                    break;
                }
            }

            if (!allNumeric)
            {
                continue;
            }

            foreach (var fields in fieldsByComponent.Values)
            {
                var value = fields[fieldIndex]?.ToString() ?? "";
                if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                {
                    fields[fieldIndex] = number;
                }
            }
        }
    }

    private static void FixGroupedDisplayValues(
        IEnumerable<ComponentRef[]> rows,
        Dictionary<int, JsonArray> fieldsByComponent,
        InteractiveBomConfig config)
    {
        var valueIndex = IndexOf(config.ShowFields, "Value");
        if (valueIndex < 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            if (row.Length == 0 || !fieldsByComponent.TryGetValue(row[0].Index, out var firstFields))
            {
                continue;
            }

            var displayValue = firstFields[valueIndex]?.DeepClone();
            foreach (var item in row)
            {
                if (fieldsByComponent.TryGetValue(item.Index, out var fields))
                {
                    fields[valueIndex] = displayValue?.DeepClone();
                }
            }
        }
    }

    private static IEnumerable<JsonArray> FilterRows(
        IEnumerable<JsonArray> rows,
        IReadOnlyList<ComponentInfo> components,
        InteractiveBomConfig config,
        string layer)
    {
        var filtered = new List<ComponentRef[]>();
        foreach (var row in rows)
        {
            var refs = row
                .Select(item => item?.AsArray())
                .Where(item => item is { Count: >= 2 })
                .Select(item => new ComponentRef(item![0]!.GetValue<string>(), item[1]!.GetValue<int>()))
                .Where(item => string.Equals(components[item.Index].Layer, layer, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Reference, NaturalReferenceComparer.Instance)
                .ThenBy(r => r.Index)
                .ToArray();

            if (refs.Length != 0)
            {
                filtered.Add(refs);
            }
        }

        return filtered
            .OrderBy(row => (IReadOnlyCollection<ComponentRef>)row, RowComparer.For(config))
            .Select(ToJsonRow);
    }

    private static JsonArray ToJsonRows(IEnumerable<JsonArray> rows)
    {
        var result = new JsonArray();
        foreach (var row in rows)
        {
            result.Add(row.DeepClone());
        }

        return result;
    }

    private static JsonArray ToJsonRow(IEnumerable<ComponentRef> refs)
    {
        var row = new JsonArray();
        foreach (var item in refs)
        {
            row.Add(new JsonArray(item.Reference, item.Index));
        }

        return row;
    }

    private static object CreateHtmlConfig(InteractiveBomConfig config) => new
    {
        dark_mode = config.DarkMode,
        show_pads = config.ShowPads,
        show_fabrication = config.ShowFabrication,
        show_silkscreen = config.ShowSilkscreen,
        highlight_pin1 = config.HighlightPin1,
        redraw_on_drag = config.RedrawOnDrag,
        board_rotation = config.BoardRotation,
        checkboxes = string.Join(',', config.Checkboxes),
        bom_view = config.BomView,
        layer_view = config.LayerView,
        offset_back_rotation = config.OffsetBackRotation,
        kicad_text_formatting = config.KicadTextFormatting,
        mark_when_checked = config.MarkWhenChecked,
        fields = config.ShowFields,
    };

    private string ReadAsset(string fileName)
    {
        var relativePath = Path.Combine("lib", "interactive-html-bom", fileName).Replace('\\', '/');
        var fileInfo = _environment.WebRootFileProvider.GetFileInfo(relativePath);
        if (fileInfo.Exists)
        {
            using var stream = fileInfo.CreateReadStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        var candidateRoots = new[]
        {
            _environment.WebRootPath,
            Path.Combine(_environment.ContentRootPath, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "SvnHub.Web", "wwwroot"),
        };

        foreach (var root in candidateRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var path = Path.Combine(root!, "lib", "interactive-html-bom", fileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        throw new FileNotFoundException(
            $"Interactive BOM asset '{fileName}' was not found under wwwroot/lib/interactive-html-bom.");
    }

    private readonly record struct ComponentRef(string Reference, int Index);

    private sealed record InteractiveBomConfig
    {
        public static InteractiveBomConfig Default { get; } = new();

        public bool DarkMode { get; init; }
        public bool ShowPads { get; init; } = true;
        public bool ShowFabrication { get; init; }
        public bool ShowSilkscreen { get; init; } = true;
        public bool RedrawOnDrag { get; init; } = true;
        public string HighlightPin1 { get; init; } = "none";
        public int BoardRotation { get; init; }
        public bool OffsetBackRotation { get; init; }
        public IReadOnlyList<string> Checkboxes { get; init; } = ["Sourced", "Placed"];
        public string MarkWhenChecked { get; init; } = "";
        public string BomView { get; init; } = "left-right";
        public string LayerView { get; init; } = "FB";
        public bool KicadTextFormatting { get; init; } = false;

        public IReadOnlyList<string> ShowFields { get; init; } = DefaultShowFields;
        public IReadOnlyList<string> GroupFields { get; init; } = DefaultGroupFields;
        public IReadOnlyList<string> ComponentSortOrder { get; init; } = DefaultComponentSortOrder;
        public IReadOnlyList<string> ComponentBlacklist { get; init; } = [];
        public bool BlacklistVirtual { get; init; } = true;
        public bool BlacklistEmptyValue { get; init; }
        public bool NormalizeFieldCase { get; init; }
        public string BoardVariantField { get; init; } = "";
        public IReadOnlyList<string> BoardVariantWhitelist { get; init; } = [];
        public IReadOnlyList<string> BoardVariantBlacklist { get; init; } = [];
        public string DnpField { get; init; } = "";
    }

    private sealed record ComponentInfo(
        string Reference,
        string Value,
        string Footprint,
        string Layer,
        string Attr,
        IReadOnlyDictionary<string, string> ExtraFields)
    {
        public static ComponentInfo FromJson(int index, JsonObject component)
        {
            var reference = GetString(component, "ref");
            if (string.IsNullOrWhiteSpace(reference))
            {
                reference = $"#{index + 1}";
            }

            var extraFields = component["extra_fields"]?.AsObject()
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString() ?? "", StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            return new ComponentInfo(
                reference,
                GetString(component, "val"),
                GetString(component, "footprint"),
                GetString(component, "layer"),
                GetString(component, "attr"),
                extraFields);
        }

        private static string GetString(JsonObject obj, string propertyName) =>
            obj.TryGetPropertyValue(propertyName, out var value) ? value?.ToString() ?? "" : "";
    }

    private sealed partial class NaturalReferenceComparer : IComparer<string>
    {
        public static readonly NaturalReferenceComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= "";
            y ??= "";
            var xParts = NaturalTokenRegex().Matches(x);
            var yParts = NaturalTokenRegex().Matches(y);
            var count = Math.Min(xParts.Count, yParts.Count);
            for (var i = 0; i < count; i++)
            {
                var xToken = xParts[i].Value;
                var yToken = yParts[i].Value;
                var xIsNumber = int.TryParse(xToken, out var xNumber);
                var yIsNumber = int.TryParse(yToken, out var yNumber);
                var cmp = xIsNumber && yIsNumber
                    ? xNumber.CompareTo(yNumber)
                    : string.Compare(xToken, yToken, StringComparison.OrdinalIgnoreCase);

                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return xParts.Count.CompareTo(yParts.Count);
        }

        [GeneratedRegex(@"\d+|\D+", RegexOptions.CultureInvariant)]
        private static partial Regex NaturalTokenRegex();
    }

    private sealed class RowComparer : IComparer<IReadOnlyCollection<ComponentRef>>
    {
        private readonly IReadOnlyList<string> _componentSortOrder;

        private RowComparer(IReadOnlyList<string> componentSortOrder)
        {
            _componentSortOrder = componentSortOrder;
        }

        public static RowComparer For(InteractiveBomConfig config) => new(config.ComponentSortOrder);

        public int Compare(IReadOnlyCollection<ComponentRef>? x, IReadOnlyCollection<ComponentRef>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var xFirst = x.FirstOrDefault().Reference ?? "";
            var yFirst = y.FirstOrDefault().Reference ?? "";
            var cmp = GetPrefixOrder(xFirst).CompareTo(GetPrefixOrder(yFirst));
            if (cmp != 0)
            {
                return cmp;
            }

            cmp = (-x.Count).CompareTo(-y.Count);
            if (cmp != 0)
            {
                return cmp;
            }

            return NaturalReferenceComparer.Instance.Compare(xFirst, yFirst);
        }

        private int GetPrefixOrder(string reference)
        {
            var prefix = ReferencePrefixRegex().Match(reference).Value;
            var index = IndexOf(_componentSortOrder, prefix);
            if (index < 0)
            {
                index = IndexOf(_componentSortOrder, "~");
            }

            return index < 0 ? int.MaxValue : index;
        }
    }

    private static (string Value, string? Unit) ComponentValue(string value, string reference)
    {
        var result = ComponentMatch(value);
        if (result is null)
        {
            return (value, null);
        }

        if (result.Value.Unit is not null)
        {
            return result.Value;
        }

        var match = ReferenceUnitRegex().Match(reference.ToLowerInvariant());
        if (!match.Success)
        {
            return result.Value;
        }

        var prefix = match.Groups[1].Value;
        var unit = prefix switch
        {
            "r" or "rv" => "R",
            "c" => "F",
            "l" => "H",
            _ => null,
        };

        return (result.Value.Value, unit);
    }

    private static (string Value, string? Unit)? ComponentMatch(string component)
    {
        component = component.Trim().ToLowerInvariant();
        var decimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        component = decimalSeparator == ","
            ? component.Replace(",", ".", StringComparison.Ordinal)
            : component.Replace(",", "", StringComparison.Ordinal);

        var match = ValueRegex().Match(component);
        if (!match.Success)
        {
            return null;
        }

        var valueText = match.Groups[1].Value;
        var prefix = match.Groups[2].Value;
        var unitText = match.Groups[3].Value;
        var post = match.Groups[4].Value;

        if (post.Length != 0 && !valueText.Contains('.', StringComparison.Ordinal))
        {
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerPart) ||
                !int.TryParse(post, NumberStyles.Integer, CultureInfo.InvariantCulture, out var postPart))
            {
                return null;
            }

            valueText = (integerPart + (postPart / Math.Pow(10, post.Length))).ToString("0.###############", CultureInfo.InvariantCulture);
        }

        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        var normalized = (value * GetPrefix(prefix)).ToString("0.000000000000000", CultureInfo.InvariantCulture);
        return (normalized, GetUnit(unitText));
    }

    private static string? GetUnit(string unit)
    {
        if (unit.Length == 0)
        {
            return null;
        }

        return unit switch
        {
            "r" or "ohms" or "ohm" or "\u03c9" or "\u2126" => "R",
            "farad" or "f" => "F",
            "henry" or "h" => "H",
            _ => null,
        };
    }

    private static double GetPrefix(string prefix)
    {
        if (prefix.Length == 0)
        {
            return 1;
        }

        return prefix switch
        {
            "pico" or "p" => 1.0e-12,
            "nano" or "n" => 1.0e-9,
            "\u03bc" or "\u00b5" or "u" or "micro" => 1.0e-6,
            "milli" or "m" => 1.0e-3,
            "kilo" or "k" => 1.0e3,
            "mega" or "meg" => 1.0e6,
            "giga" or "g" => 1.0e9,
            _ => 1,
        };
    }

    private static bool IsDigitsOnly(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOf(IReadOnlyList<string> items, string value)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"^[A-Z]*", RegexOptions.CultureInvariant)]
    private static partial Regex RefLettersPrefixRegex();

    [GeneratedRegex(@"^[^0-9]*", RegexOptions.CultureInvariant)]
    private static partial Regex ReferencePrefixRegex();

    [GeneratedRegex(@"^(r|rv|c|l)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceUnitRegex();

    [GeneratedRegex(@"^([0-9\.]+)(pico|p|nano|n|\u03bc|\u00b5|u|micro|milli|m|kilo|k|mega|meg|giga|g)*(r|ohms|ohm|\u03c9|\u2126|farad|f|henry|h)*(\d*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ValueRegex();
}
