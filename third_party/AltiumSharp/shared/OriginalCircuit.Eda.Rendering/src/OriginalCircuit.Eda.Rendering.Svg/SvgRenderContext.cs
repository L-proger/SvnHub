using System.Globalization;
using System.Text;
using System.Xml.Linq;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Eda.Rendering.Svg;

/// <summary>
/// An <see cref="IRenderContext"/> implementation that builds an SVG document
/// using <see cref="System.Xml.Linq"/> (no external dependencies).
/// </summary>
public sealed class SvgRenderContext : IRenderContext
{
    private readonly double _width;
    private readonly double _height;
    private readonly XElement _root;
    private XElement _currentGroup;
    private readonly Stack<XElement> _groupStack = new();
    private int _clipId;

    /// <summary>
    /// Initializes a new SVG render context with the specified canvas dimensions.
    /// </summary>
    /// <param name="width">Width of the SVG viewport.</param>
    /// <param name="height">Height of the SVG viewport.</param>
    public SvgRenderContext(double width, double height)
    {
        _width = width;
        _height = height;
        _root = new XElement("svg",
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("viewBox", $"0 0 {Fmt(width)} {Fmt(height)}"));
        _currentGroup = _root;
    }

    private static string Fmt(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private static string ToSvgColor(uint argb)
    {
        var r = (argb >> 16) & 0xFF;
        var g = (argb >> 8) & 0xFF;
        var b = argb & 0xFF;
        return $"rgb({r},{g},{b})";
    }

    private static string ToOpacity(uint argb)
    {
        var a = (argb >> 24) & 0xFF;
        return (a / 255.0).ToString("F3", CultureInfo.InvariantCulture);
    }

    private static bool IsOpaque(uint argb) => ((argb >> 24) & 0xFF) == 0xFF;

    private static void AddOpacity(XElement el, uint argb, string attrName = "opacity")
    {
        if (!IsOpaque(argb))
            el.Add(new XAttribute(attrName, ToOpacity(argb)));
    }

    private static string? GetDashArray(LineStyle style)
    {
        return style switch
        {
            LineStyle.Dash => "8,4",
            LineStyle.Dot => "2,4",
            LineStyle.DashDot => "8,4,2,4",
            LineStyle.DashDotDot => "8,4,2,4,2,4",
            _ => null
        };
    }

    private void Append(XElement el) => _currentGroup.Add(el);

    /// <inheritdoc />
    public void Clear(uint argbColor)
    {
        var bg = new XElement("rect",
            new XAttribute("x", "0"),
            new XAttribute("y", "0"),
            new XAttribute("width", Fmt(_width)),
            new XAttribute("height", Fmt(_height)),
            new XAttribute("fill", ToSvgColor(argbColor)));
        AddOpacity(bg, argbColor);
        _root.AddFirst(bg);
    }

    /// <inheritdoc />
    public void DrawLine(double x1, double y1, double x2, double y2, uint color, double width,
        LineStyle style = LineStyle.Solid)
    {
        var el = new XElement("line",
            new XAttribute("x1", Fmt(x1)),
            new XAttribute("y1", Fmt(y1)),
            new XAttribute("x2", Fmt(x2)),
            new XAttribute("y2", Fmt(y2)),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("stroke-linecap", "round"));
        AddOpacity(el, color);
        var dash = GetDashArray(style);
        if (dash != null) el.Add(new XAttribute("stroke-dasharray", dash));
        Append(el);
    }

    /// <inheritdoc />
    public void DrawArc(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color, double width)
    {
        var startRad = startAngle * Math.PI / 180.0;
        var sweepRad = sweepAngle * Math.PI / 180.0;

        var x1 = cx + rx * Math.Cos(startRad);
        var y1 = cy + ry * Math.Sin(startRad);
        var x2 = cx + rx * Math.Cos(startRad + sweepRad);
        var y2 = cy + ry * Math.Sin(startRad + sweepRad);

        var largeArc = Math.Abs(sweepAngle) > 180 ? 1 : 0;
        var sweepFlag = sweepAngle > 0 ? 1 : 0;

        if (Math.Abs(Math.Abs(sweepAngle) - 360) < 0.01)
        {
            var el = new XElement("ellipse",
                new XAttribute("cx", Fmt(cx)),
                new XAttribute("cy", Fmt(cy)),
                new XAttribute("rx", Fmt(rx)),
                new XAttribute("ry", Fmt(ry)),
                new XAttribute("stroke", ToSvgColor(color)),
                new XAttribute("stroke-width", Fmt(width)),
                new XAttribute("fill", "none"));
            AddOpacity(el, color);
            Append(el);
            return;
        }

        var d = $"M {Fmt(x1)} {Fmt(y1)} A {Fmt(rx)} {Fmt(ry)} 0 {largeArc} {sweepFlag} {Fmt(x2)} {Fmt(y2)}";
        var path = new XElement("path",
            new XAttribute("d", d),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("fill", "none"),
            new XAttribute("stroke-linecap", "round"));
        AddOpacity(path, color);
        Append(path);
    }

    /// <inheritdoc />
    public void DrawRectangle(double x, double y, double width, double height, uint color, double lineWidth)
    {
        var el = new XElement("rect",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(lineWidth)),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillRectangle(double x, double y, double width, double height, uint color)
    {
        var el = new XElement("rect",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("fill", ToSvgColor(color)));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawRoundedRectangle(double x, double y, double width, double height,
        double cornerRadiusX, double cornerRadiusY, uint color, double lineWidth)
    {
        var el = new XElement("rect",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("rx", Fmt(cornerRadiusX)),
            new XAttribute("ry", Fmt(cornerRadiusY)),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(lineWidth)),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillRoundedRectangle(double x, double y, double width, double height,
        double cornerRadius, uint color)
    {
        var el = new XElement("rect",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("rx", Fmt(cornerRadius)),
            new XAttribute("ry", Fmt(cornerRadius)),
            new XAttribute("fill", ToSvgColor(color)));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawEllipse(double cx, double cy, double rx, double ry, uint color, double width)
    {
        var el = new XElement("ellipse",
            new XAttribute("cx", Fmt(cx)),
            new XAttribute("cy", Fmt(cy)),
            new XAttribute("rx", Fmt(rx)),
            new XAttribute("ry", Fmt(ry)),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillEllipse(double cx, double cy, double rx, double ry, uint color)
    {
        var el = new XElement("ellipse",
            new XAttribute("cx", Fmt(cx)),
            new XAttribute("cy", Fmt(cy)),
            new XAttribute("rx", Fmt(rx)),
            new XAttribute("ry", Fmt(ry)),
            new XAttribute("fill", ToSvgColor(color)));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawPolygon(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color, double width)
    {
        var points = BuildPointsString(xPoints, yPoints);
        var el = new XElement("polygon",
            new XAttribute("points", points),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillPolygon(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color)
    {
        var points = BuildPointsString(xPoints, yPoints);
        var el = new XElement("polygon",
            new XAttribute("points", points),
            new XAttribute("fill", ToSvgColor(color)));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillContours(IReadOnlyList<(double[] X, double[] Y)> contours, uint color)
    {
        if (contours == null || contours.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var (xs, ys) in contours)
        {
            if (xs == null || ys == null || xs.Length < 3 || xs.Length != ys.Length) continue;
            sb.Append('M').Append(Fmt(xs[0])).Append(' ').Append(Fmt(ys[0]));
            for (int i = 1; i < xs.Length; i++)
                sb.Append('L').Append(Fmt(xs[i])).Append(' ').Append(Fmt(ys[i]));
            sb.Append('Z');
        }
        if (sb.Length == 0) return;

        var el = new XElement("path",
            new XAttribute("d", sb.ToString()),
            new XAttribute("fill", ToSvgColor(color)),
            new XAttribute("fill-rule", "evenodd"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawPolyline(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color, double width,
        LineStyle style = LineStyle.Solid)
    {
        var points = BuildPointsString(xPoints, yPoints);
        var el = new XElement("polyline",
            new XAttribute("points", points),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("stroke-linecap", "round"),
            new XAttribute("stroke-linejoin", "round"),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        var dash = GetDashArray(style);
        if (dash != null) el.Add(new XAttribute("stroke-dasharray", dash));
        Append(el);
    }

    /// <inheritdoc />
    public void DrawBezier(double x0, double y0, double x1, double y1, double x2, double y2,
        double x3, double y3, uint color, double width)
    {
        var d = $"M {Fmt(x0)} {Fmt(y0)} C {Fmt(x1)} {Fmt(y1)}, {Fmt(x2)} {Fmt(y2)}, {Fmt(x3)} {Fmt(y3)}";
        var el = new XElement("path",
            new XAttribute("d", d),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(width)),
            new XAttribute("stroke-linecap", "round"),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void FillPie(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color)
    {
        var path = BuildPiePath(cx, cy, rx, ry, startAngle, sweepAngle);
        var el = new XElement("path",
            new XAttribute("d", path),
            new XAttribute("fill", ToSvgColor(color)));
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawPie(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color, double lineWidth)
    {
        var path = BuildPiePath(cx, cy, rx, ry, startAngle, sweepAngle);
        var el = new XElement("path",
            new XAttribute("d", path),
            new XAttribute("stroke", ToSvgColor(color)),
            new XAttribute("stroke-width", Fmt(lineWidth)),
            new XAttribute("fill", "none"));
        AddOpacity(el, color);
        Append(el);
    }

    private static string BuildPiePath(double cx, double cy, double rx, double ry,
        double startAngle, double sweepAngle)
    {
        var startRad = startAngle * Math.PI / 180.0;
        var endRad = (startAngle + sweepAngle) * Math.PI / 180.0;
        var x1 = cx + rx * Math.Cos(startRad);
        var y1 = cy + ry * Math.Sin(startRad);
        var x2 = cx + rx * Math.Cos(endRad);
        var y2 = cy + ry * Math.Sin(endRad);
        var largeArc = Math.Abs(sweepAngle) > 180 ? 1 : 0;
        var sweepFlag = sweepAngle > 0 ? 1 : 0;
        return $"M {Fmt(cx)} {Fmt(cy)} L {Fmt(x1)} {Fmt(y1)} A {Fmt(rx)} {Fmt(ry)} 0 {largeArc} {sweepFlag} {Fmt(x2)} {Fmt(y2)} Z";
    }

    /// <inheritdoc />
    public void DrawText(string text, double x, double y, double fontSize, uint color,
        string fontFamily = "Arial")
    {
        var el = new XElement("text",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("font-size", Fmt(fontSize)),
            new XAttribute("font-family", fontFamily),
            new XAttribute("fill", ToSvgColor(color)),
            new XAttribute("dominant-baseline", "auto"),
            text);
        AddOpacity(el, color);
        Append(el);
    }

    /// <inheritdoc />
    public void DrawText(string text, double x, double y, double fontSize, uint color,
        TextRenderOptions options)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Match the raster backend's metric-based vertical positioning rather than relying on
        // dominant-baseline keywords, which differ across SVG renderers and do not align with the
        // (ascent + descent)/2 cap-box centre Skia uses. Estimate ascent/descent from the font size
        // (ascent ~= 0.8*size, descent ~= 0.2*size, matching MeasureText's 1.16 line factor) and
        // emit an explicit baseline y with dominant-baseline=auto so both backends register text
        // at the same height.
        const double ascentFactor = 0.8;
        const double descentFactor = 0.2;
        var baselineY = options.VerticalAlignment switch
        {
            TextVAlign.Top => y + ascentFactor * fontSize,
            TextVAlign.Middle => y + (ascentFactor - descentFactor) / 2 * fontSize,
            TextVAlign.Bottom => y - descentFactor * fontSize,
            _ => y
        };

        var el = new XElement("text",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(baselineY)),
            new XAttribute("font-size", Fmt(fontSize)),
            new XAttribute("font-family", options.FontFamily),
            new XAttribute("fill", ToSvgColor(color)));

        if (options.Bold) el.Add(new XAttribute("font-weight", "bold"));
        if (options.Italic) el.Add(new XAttribute("font-style", "italic"));

        el.Add(new XAttribute("text-anchor", options.HorizontalAlignment switch
        {
            TextHAlign.Center => "middle",
            TextHAlign.Right => "end",
            _ => "start"
        }));

        el.Add(new XAttribute("dominant-baseline", "auto"));

        AddOpacity(el, color);
        el.Add(text);
        Append(el);
    }

    /// <inheritdoc />
    public TextMetrics MeasureText(string text, double fontSize, TextRenderOptions? options = null)
    {
        if (string.IsNullOrEmpty(text)) return new TextMetrics(0, 0);

        // SVG has no font engine here, so estimate from per-character advance widths
        // (Helvetica/Arial AFM metrics) rather than a flat average — far better for
        // word-wrap, centering and overline placement.
        double emWidth = 0;
        foreach (var ch in text)
            emWidth += AdvanceWidthEm(ch);

        var boldFactor = options?.Bold == true ? 1.06 : 1.0;
        var width = emWidth * fontSize * boldFactor;
        var height = fontSize * 1.16; // ~ascent+descent for Arial
        return new TextMetrics(width, height);
    }

    private static double AdvanceWidthEm(char c)
    {
        if (c >= 32 && c <= 126) return HelveticaWidths[c - 32] / 1000.0;
        return 0.5; // reasonable fallback for non-ASCII
    }

    // Helvetica/Arial advance widths (per-1000 em) for printable ASCII 32..126.
    private static readonly short[] HelveticaWidths =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    };

    /// <inheritdoc />
    public void DrawImage(ReadOnlySpan<byte> imageData, double x, double y, double width, double height)
    {
        if (imageData.IsEmpty) return;
        var base64 = Convert.ToBase64String(imageData);
        var mime = "image/png";
        if (imageData.Length >= 3 && imageData[0] == 0xFF && imageData[1] == 0xD8)
            mime = "image/jpeg";
        else if (imageData.Length >= 4 && imageData[0] == 'B' && imageData[1] == 'M')
            mime = "image/bmp";

        var el = new XElement("image",
            new XAttribute("x", Fmt(x)),
            new XAttribute("y", Fmt(y)),
            new XAttribute("width", Fmt(width)),
            new XAttribute("height", Fmt(height)),
            new XAttribute("href", $"data:{mime};base64,{base64}"));
        Append(el);
    }

    /// <inheritdoc />
    public void SetClipRect(double x, double y, double w, double h)
    {
        var clipId = $"clip{++_clipId}";
        var defs = _root.Element("defs") ?? new XElement("defs");
        if (defs.Parent == null) _root.AddFirst(defs);

        var clipPath = new XElement("clipPath",
            new XAttribute("id", clipId),
            new XElement("rect",
                new XAttribute("x", Fmt(x)),
                new XAttribute("y", Fmt(y)),
                new XAttribute("width", Fmt(w)),
                new XAttribute("height", Fmt(h))));
        defs.Add(clipPath);

        var g = new XElement("g",
            new XAttribute("clip-path", $"url(#{clipId})"));
        _currentGroup.Add(g);
        _groupStack.Push(_currentGroup);
        _currentGroup = g;
    }

    /// <inheritdoc />
    public void SetClipPath(IReadOnlyList<(double[] X, double[] Y)> contours)
    {
        if (contours == null || contours.Count == 0) return;

        var sb = new StringBuilder();
        foreach (var (xs, ys) in contours)
        {
            if (xs == null || ys == null || xs.Length < 3 || xs.Length != ys.Length) continue;
            sb.Append('M').Append(Fmt(xs[0])).Append(' ').Append(Fmt(ys[0]));
            for (int i = 1; i < xs.Length; i++)
                sb.Append('L').Append(Fmt(xs[i])).Append(' ').Append(Fmt(ys[i]));
            sb.Append('Z');
        }
        if (sb.Length == 0) return;

        var clipId = $"clip{++_clipId}";
        var defs = _root.Element("defs") ?? new XElement("defs");
        if (defs.Parent == null) _root.AddFirst(defs);

        // Default clip-rule is non-zero, so overlapping contours union (no even-odd cancellation).
        var clipPath = new XElement("clipPath",
            new XAttribute("id", clipId),
            new XElement("path", new XAttribute("d", sb.ToString())));
        defs.Add(clipPath);

        // Apply to the current group/state; pair with SaveState/RestoreState to scope it.
        _currentGroup.SetAttributeValue("clip-path", $"url(#{clipId})");
    }

    /// <inheritdoc />
    public void ResetClip()
    {
        if (_groupStack.Count > 0)
            _currentGroup = _groupStack.Pop();
    }

    /// <inheritdoc />
    public void SaveState()
    {
        var g = new XElement("g");
        _currentGroup.Add(g);
        _groupStack.Push(_currentGroup);
        _currentGroup = g;
    }

    /// <inheritdoc />
    public void RestoreState()
    {
        if (_groupStack.Count > 0)
            _currentGroup = _groupStack.Pop();
    }

    /// <inheritdoc />
    public void BeginGroup(string id)
    {
        var g = new XElement("g", new XAttribute("id", id));
        _currentGroup.Add(g);
        _groupStack.Push(_currentGroup);
        _currentGroup = g;
    }

    /// <inheritdoc />
    public void EndGroup()
    {
        if (_groupStack.Count > 0)
            _currentGroup = _groupStack.Pop();
    }

    /// <inheritdoc />
    public void Translate(double dx, double dy)
    {
        AppendTransform($"translate({Fmt(dx)},{Fmt(dy)})");
    }

    /// <inheritdoc />
    public void Rotate(double angleDegrees)
    {
        AppendTransform($"rotate({Fmt(angleDegrees)})");
    }

    /// <inheritdoc />
    public void Scale(double sx, double sy)
    {
        AppendTransform($"scale({Fmt(sx)},{Fmt(sy)})");
    }

    private void AppendTransform(string transform)
    {
        var existing = _currentGroup.Attribute("transform")?.Value ?? "";
        _currentGroup.SetAttributeValue("transform",
            string.IsNullOrEmpty(existing) ? transform : $"{existing} {transform}");
    }

    private static string BuildPointsString(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints)
    {
        var sb = new StringBuilder(xPoints.Length * 16);
        for (int i = 0; i < xPoints.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(Fmt(xPoints[i]));
            sb.Append(',');
            sb.Append(Fmt(yPoints[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns the complete SVG document as a string, including the xmlns attribute.
    /// </summary>
    /// <returns>The SVG document as a string.</returns>
    public string ToSvgString()
    {
        var xml = _root.ToString();
        return xml.Replace("<svg ", "<svg xmlns=\"http://www.w3.org/2000/svg\" ");
    }

    /// <summary>
    /// Writes the SVG document with XML declaration to the specified stream.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    public void WriteTo(Stream stream)
    {
        var svg = ToSvgString();
        using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        writer.Write(svg);
    }
}
