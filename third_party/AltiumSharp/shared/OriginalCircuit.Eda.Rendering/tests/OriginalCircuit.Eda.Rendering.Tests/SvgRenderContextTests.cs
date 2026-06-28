using System.Xml.Linq;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Rendering;
using OriginalCircuit.Eda.Rendering.Svg;

namespace OriginalCircuit.Eda.Rendering.Tests;

public sealed class SvgRenderContextTests
{
    private static XDocument Render(Action<SvgRenderContext> draw, double width = 200, double height = 200)
    {
        var ctx = new SvgRenderContext(width, height);
        ctx.Clear(ColorHelper.White);
        draw(ctx);
        var svg = ctx.ToSvgString();
        return XDocument.Parse(svg);
    }

    private static XNamespace Ns => "http://www.w3.org/2000/svg";

    private static int Count(XDocument doc, string element) =>
        doc.Descendants(Ns + element).Count();

    // ── Structure ─────────────────────────────────────────────────────

    [Fact]
    public void SvgOutput_HasRootSvgElement()
    {
        var doc = Render(_ => { });
        Assert.Equal("svg", doc.Root!.Name.LocalName);
    }

    [Fact]
    public void SvgOutput_HasCorrectNamespace()
    {
        var doc = Render(_ => { });
        Assert.Equal("http://www.w3.org/2000/svg", doc.Root!.Name.NamespaceName);
    }

    [Fact]
    public void SvgOutput_HasWidthHeightViewBox()
    {
        var doc = Render(_ => { }, width: 512, height: 256);
        var root = doc.Root!;

        Assert.Equal("512", root.Attribute("width")!.Value);
        Assert.Equal("256", root.Attribute("height")!.Value);
        Assert.Equal("0 0 512 256", root.Attribute("viewBox")!.Value);
    }

    [Fact]
    public void Clear_ProducesBackgroundRect()
    {
        var doc = Render(_ => { });
        var rects = doc.Descendants(Ns + "rect").ToList();
        Assert.Single(rects);
        Assert.Equal("rgb(255,255,255)", rects[0].Attribute("fill")!.Value);
    }

    // ── DrawLine ──────────────────────────────────────────────────────

    [Fact]
    public void DrawLine_ProducesLineElement()
    {
        var doc = Render(ctx => ctx.DrawLine(10, 20, 30, 40, ColorHelper.Red, 2));
        Assert.Equal(1, Count(doc, "line"));
    }

    [Fact]
    public void DrawLine_HasCorrectAttributes()
    {
        var doc = Render(ctx => ctx.DrawLine(10, 20, 30, 40, ColorHelper.Red, 2));
        var line = doc.Descendants(Ns + "line").Single();

        Assert.Equal("10", line.Attribute("x1")!.Value);
        Assert.Equal("20", line.Attribute("y1")!.Value);
        Assert.Equal("30", line.Attribute("x2")!.Value);
        Assert.Equal("40", line.Attribute("y2")!.Value);
        Assert.Equal("rgb(255,0,0)", line.Attribute("stroke")!.Value);
        Assert.Equal("2", line.Attribute("stroke-width")!.Value);
    }

    [Fact]
    public void DrawLine_SolidStyle_NoDashArray()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1, LineStyle.Solid));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Null(line.Attribute("stroke-dasharray"));
    }

    [Fact]
    public void DrawLine_DashStyle_HasDashArray()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1, LineStyle.Dash));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Equal("8,4", line.Attribute("stroke-dasharray")!.Value);
    }

    [Fact]
    public void DrawLine_DotStyle_HasDashArray()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1, LineStyle.Dot));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Equal("2,4", line.Attribute("stroke-dasharray")!.Value);
    }

    [Fact]
    public void DrawLine_DashDotStyle_HasDashArray()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1, LineStyle.DashDot));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Equal("8,4,2,4", line.Attribute("stroke-dasharray")!.Value);
    }

    [Fact]
    public void DrawLine_DashDotDotStyle_HasDashArray()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1, LineStyle.DashDotDot));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Equal("8,4,2,4,2,4", line.Attribute("stroke-dasharray")!.Value);
    }

    [Fact]
    public void DrawLine_MultipleLines_ProducesCorrectCount()
    {
        var doc = Render(ctx =>
        {
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.DrawLine(20, 20, 30, 30, ColorHelper.Black, 1);
            ctx.DrawLine(40, 40, 50, 50, ColorHelper.Black, 1);
        });
        Assert.Equal(3, Count(doc, "line"));
    }

    [Fact]
    public void DrawLine_SemiTransparent_HasOpacityAttribute()
    {
        var semiTransparent = 0x80FF0000u; // alpha=128
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, semiTransparent, 1));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.NotNull(line.Attribute("opacity"));
    }

    [Fact]
    public void DrawLine_Opaque_NoOpacityAttribute()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.Red, 1));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Null(line.Attribute("opacity"));
    }

    // ── DrawRectangle / FillRectangle ─────────────────────────────────

    [Fact]
    public void DrawRectangle_ProducesRectWithNoFill()
    {
        var doc = Render(ctx => ctx.DrawRectangle(10, 20, 100, 50, ColorHelper.Black, 2));
        // 1 background + 1 drawn = 2 rects
        Assert.Equal(2, Count(doc, "rect"));
        var drawn = doc.Descendants(Ns + "rect").Last();
        Assert.Equal("none", drawn.Attribute("fill")!.Value);
        Assert.Equal("2", drawn.Attribute("stroke-width")!.Value);
    }

    [Fact]
    public void FillRectangle_ProducesRectWithFill()
    {
        var doc = Render(ctx => ctx.FillRectangle(10, 20, 100, 50, ColorHelper.Blue));
        Assert.Equal(2, Count(doc, "rect")); // bg + fill
        var filled = doc.Descendants(Ns + "rect").Last();
        Assert.Equal("rgb(0,0,255)", filled.Attribute("fill")!.Value);
        Assert.Null(filled.Attribute("stroke"));
    }

    [Fact]
    public void DrawAndFillRectangle_ProducesThreeRects()
    {
        var doc = Render(ctx =>
        {
            ctx.FillRectangle(10, 20, 100, 50, ColorHelper.Blue);
            ctx.DrawRectangle(10, 20, 100, 50, ColorHelper.Black, 1);
        });
        Assert.Equal(3, Count(doc, "rect")); // bg + fill + stroke
    }

    // ── DrawRoundedRectangle / FillRoundedRectangle ───────────────────

    [Fact]
    public void DrawRoundedRectangle_HasRxRyAttributes()
    {
        var doc = Render(ctx => ctx.DrawRoundedRectangle(0, 0, 100, 50, 8, 6, ColorHelper.Black, 1));
        var rect = doc.Descendants(Ns + "rect").Last();
        Assert.Equal("8", rect.Attribute("rx")!.Value);
        Assert.Equal("6", rect.Attribute("ry")!.Value);
        Assert.Equal("none", rect.Attribute("fill")!.Value);
    }

    [Fact]
    public void FillRoundedRectangle_HasRxRyAttributes()
    {
        var doc = Render(ctx => ctx.FillRoundedRectangle(0, 0, 100, 50, 10, ColorHelper.Green));
        var rect = doc.Descendants(Ns + "rect").Last();
        Assert.Equal("10", rect.Attribute("rx")!.Value);
        Assert.Equal("10", rect.Attribute("ry")!.Value);
        Assert.Equal("rgb(0,255,0)", rect.Attribute("fill")!.Value);
    }

    // ── DrawEllipse / FillEllipse ─────────────────────────────────────

    [Fact]
    public void DrawEllipse_ProducesEllipseWithNoFill()
    {
        var doc = Render(ctx => ctx.DrawEllipse(50, 50, 30, 20, ColorHelper.Red, 2));
        Assert.Equal(1, Count(doc, "ellipse"));
        var el = doc.Descendants(Ns + "ellipse").Single();
        Assert.Equal("none", el.Attribute("fill")!.Value);
        Assert.Equal("50", el.Attribute("cx")!.Value);
        Assert.Equal("50", el.Attribute("cy")!.Value);
        Assert.Equal("30", el.Attribute("rx")!.Value);
        Assert.Equal("20", el.Attribute("ry")!.Value);
    }

    [Fact]
    public void FillEllipse_ProducesEllipseWithFill()
    {
        var doc = Render(ctx => ctx.FillEllipse(50, 50, 30, 20, ColorHelper.Green));
        Assert.Equal(1, Count(doc, "ellipse"));
        var el = doc.Descendants(Ns + "ellipse").Single();
        Assert.Equal("rgb(0,255,0)", el.Attribute("fill")!.Value);
    }

    [Fact]
    public void DrawAndFillEllipse_ProducesTwoEllipses()
    {
        var doc = Render(ctx =>
        {
            ctx.FillEllipse(50, 50, 30, 20, ColorHelper.Green);
            ctx.DrawEllipse(50, 50, 30, 20, ColorHelper.Black, 1);
        });
        Assert.Equal(2, Count(doc, "ellipse"));
    }

    // ── DrawArc ───────────────────────────────────────────────────────

    [Fact]
    public void DrawArc_FullCircle_ProducesEllipse()
    {
        var doc = Render(ctx => ctx.DrawArc(100, 100, 50, 50, 0, 360, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "ellipse"));
        Assert.Equal(0, Count(doc, "path"));
    }

    [Fact]
    public void DrawArc_PartialArc_ProducesPath()
    {
        var doc = Render(ctx => ctx.DrawArc(100, 100, 50, 50, 0, 90, ColorHelper.Black, 2));
        Assert.Equal(0, Count(doc, "ellipse"));
        Assert.Equal(1, Count(doc, "path"));
        var path = doc.Descendants(Ns + "path").Single();
        Assert.Contains("A", path.Attribute("d")!.Value);
    }

    [Fact]
    public void DrawArc_Semicircle_ProducesPath()
    {
        var doc = Render(ctx => ctx.DrawArc(100, 100, 50, 50, 0, 180, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "path"));
    }

    [Fact]
    public void DrawArc_NearlyFullCircle_ProducesEllipse()
    {
        // 359.99 degrees — close enough to 360 to trigger ellipse path
        var doc = Render(ctx => ctx.DrawArc(100, 100, 50, 50, 0, 359.995, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "ellipse"));
    }

    [Fact]
    public void DrawArc_LargeArc_PathHasLargeArcFlag()
    {
        var doc = Render(ctx => ctx.DrawArc(100, 100, 50, 50, 0, 270, ColorHelper.Black, 2));
        var path = doc.Descendants(Ns + "path").Single();
        var d = path.Attribute("d")!.Value;
        // Large arc flag should be 1 for sweep > 180
        Assert.Matches(@"A\s+\S+\s+\S+\s+0\s+1", d);
    }

    // ── DrawPolygon / FillPolygon ─────────────────────────────────────

    [Fact]
    public void DrawPolygon_ProducesPolygonWithNoFill()
    {
        double[] xs = [10, 50, 90];
        double[] ys = [90, 10, 90];
        var doc = Render(ctx => ctx.DrawPolygon(xs, ys, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "polygon"));
        var el = doc.Descendants(Ns + "polygon").Single();
        Assert.Equal("none", el.Attribute("fill")!.Value);
        Assert.Contains("10,90", el.Attribute("points")!.Value);
    }

    [Fact]
    public void FillPolygon_ProducesPolygonWithFill()
    {
        double[] xs = [10, 50, 90];
        double[] ys = [90, 10, 90];
        var doc = Render(ctx => ctx.FillPolygon(xs, ys, ColorHelper.Red));
        Assert.Equal(1, Count(doc, "polygon"));
        var el = doc.Descendants(Ns + "polygon").Single();
        Assert.Equal("rgb(255,0,0)", el.Attribute("fill")!.Value);
    }

    [Fact]
    public void DrawAndFillPolygon_ProducesTwoPolygons()
    {
        double[] xs = [10, 50, 90];
        double[] ys = [90, 10, 90];
        var doc = Render(ctx =>
        {
            ctx.FillPolygon(xs, ys, ColorHelper.Red);
            ctx.DrawPolygon(xs, ys, ColorHelper.Black, 1);
        });
        Assert.Equal(2, Count(doc, "polygon"));
    }

    // ── DrawPolyline ──────────────────────────────────────────────────

    [Fact]
    public void DrawPolyline_ProducesPolylineElement()
    {
        double[] xs = [10, 50, 90, 130];
        double[] ys = [50, 10, 90, 50];
        var doc = Render(ctx => ctx.DrawPolyline(xs, ys, ColorHelper.Blue, 2));
        Assert.Equal(1, Count(doc, "polyline"));
        var el = doc.Descendants(Ns + "polyline").Single();
        Assert.Equal("none", el.Attribute("fill")!.Value);
    }

    [Fact]
    public void DrawPolyline_HasCorrectPointCount()
    {
        double[] xs = [0, 10, 20, 30, 40];
        double[] ys = [0, 10, 0, 10, 0];
        var doc = Render(ctx => ctx.DrawPolyline(xs, ys, ColorHelper.Black, 1));
        var el = doc.Descendants(Ns + "polyline").Single();
        var points = el.Attribute("points")!.Value.Split(' ');
        Assert.Equal(5, points.Length);
    }

    [Fact]
    public void DrawPolyline_DashStyle_HasDashArray()
    {
        double[] xs = [0, 100];
        double[] ys = [0, 100];
        var doc = Render(ctx => ctx.DrawPolyline(xs, ys, ColorHelper.Black, 1, LineStyle.Dash));
        var el = doc.Descendants(Ns + "polyline").Single();
        Assert.Equal("8,4", el.Attribute("stroke-dasharray")!.Value);
    }

    // ── DrawBezier ────────────────────────────────────────────────────

    [Fact]
    public void DrawBezier_ProducesPathWithCubicCommand()
    {
        var doc = Render(ctx => ctx.DrawBezier(0, 0, 30, 60, 70, 60, 100, 0, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "path"));
        var path = doc.Descendants(Ns + "path").Single();
        var d = path.Attribute("d")!.Value;
        Assert.Contains("M", d);
        Assert.Contains("C", d);
        Assert.Equal("none", path.Attribute("fill")!.Value);
    }

    // ── FillPie / DrawPie ─────────────────────────────────────────────

    [Fact]
    public void FillPie_ProducesPathWithArcCommand()
    {
        var doc = Render(ctx => ctx.FillPie(100, 100, 50, 50, 0, 90, ColorHelper.Green));
        Assert.Equal(1, Count(doc, "path"));
        var path = doc.Descendants(Ns + "path").Single();
        var d = path.Attribute("d")!.Value;
        Assert.Contains("L", d);
        Assert.Contains("A", d);
        Assert.Contains("Z", d);
        Assert.Equal("rgb(0,255,0)", path.Attribute("fill")!.Value);
    }

    [Fact]
    public void DrawPie_ProducesPathWithNoFill()
    {
        var doc = Render(ctx => ctx.DrawPie(100, 100, 50, 50, 0, 90, ColorHelper.Black, 2));
        Assert.Equal(1, Count(doc, "path"));
        var path = doc.Descendants(Ns + "path").Single();
        Assert.Equal("none", path.Attribute("fill")!.Value);
        Assert.NotNull(path.Attribute("stroke"));
    }

    [Fact]
    public void DrawAndFillPie_ProducesTwoPaths()
    {
        var doc = Render(ctx =>
        {
            ctx.FillPie(100, 100, 50, 50, 0, 90, ColorHelper.Green);
            ctx.DrawPie(100, 100, 50, 50, 0, 90, ColorHelper.Black, 1);
        });
        Assert.Equal(2, Count(doc, "path"));
    }

    // ── DrawText ──────────────────────────────────────────────────────

    [Fact]
    public void DrawText_SimpleOverload_ProducesTextElement()
    {
        var doc = Render(ctx => ctx.DrawText("Hello", 10, 20, 14, ColorHelper.Black));
        Assert.Equal(1, Count(doc, "text"));
        var text = doc.Descendants(Ns + "text").Single();
        Assert.Equal("Hello", text.Value);
        Assert.Equal("14", text.Attribute("font-size")!.Value);
    }

    [Fact]
    public void DrawText_WithOptions_ProducesTextElement()
    {
        var opts = new TextRenderOptions
        {
            FontFamily = "Courier",
            Bold = true,
            Italic = true,
            HorizontalAlignment = TextHAlign.Center,
            VerticalAlignment = TextVAlign.Middle
        };
        var doc = Render(ctx => ctx.DrawText("Test", 50, 50, 12, ColorHelper.Black, opts));
        var text = doc.Descendants(Ns + "text").Single();
        Assert.Equal("bold", text.Attribute("font-weight")!.Value);
        Assert.Equal("italic", text.Attribute("font-style")!.Value);
        Assert.Equal("middle", text.Attribute("text-anchor")!.Value);
        // Vertical alignment is applied via an explicit baseline y, not a dominant-baseline keyword
        // (those differ across SVG renderers); the attribute stays "auto" to match the Skia backend.
        Assert.Equal("auto", text.Attribute("dominant-baseline")!.Value);
    }

    [Fact]
    public void DrawText_WithOptions_EmptyText_ProducesNoElement()
    {
        var opts = new TextRenderOptions();
        var doc = Render(ctx => ctx.DrawText("", 50, 50, 12, ColorHelper.Black, opts));
        Assert.Equal(0, Count(doc, "text"));
    }

    [Theory]
    [InlineData(TextHAlign.Left, "start")]
    [InlineData(TextHAlign.Center, "middle")]
    [InlineData(TextHAlign.Right, "end")]
    public void DrawText_HorizontalAlignment_MapsCorrectly(TextHAlign align, string expected)
    {
        var opts = new TextRenderOptions { HorizontalAlignment = align };
        var doc = Render(ctx => ctx.DrawText("X", 50, 50, 12, ColorHelper.Black, opts));
        var text = doc.Descendants(Ns + "text").Single();
        Assert.Equal(expected, text.Attribute("text-anchor")!.Value);
    }

    [Theory]
    [InlineData(TextVAlign.Top)]
    [InlineData(TextVAlign.Middle)]
    [InlineData(TextVAlign.Bottom)]
    [InlineData(TextVAlign.Baseline)]
    public void DrawText_VerticalAlignment_ShiftsBaselineY(TextVAlign align)
    {
        // The SVG backend keeps dominant-baseline="auto" and instead shifts the baseline y to match the
        // Skia backend's metric-based vertical positioning (ascent ~0.8em, descent ~0.2em).
        var opts = new TextRenderOptions { VerticalAlignment = align };
        var doc = Render(ctx => ctx.DrawText("X", 50, 50, 12, ColorHelper.Black, opts));
        var text = doc.Descendants(Ns + "text").Single();

        Assert.Equal("auto", text.Attribute("dominant-baseline")!.Value);

        double expectedY = align switch
        {
            TextVAlign.Top => 50 + 0.8 * 12,
            TextVAlign.Middle => 50 + (0.8 - 0.2) / 2 * 12,
            TextVAlign.Bottom => 50 - 0.2 * 12,
            _ => 50, // Baseline
        };
        var actualY = double.Parse(text.Attribute("y")!.Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expectedY, actualY, 3);
    }

    // ── DrawImage ─────────────────────────────────────────────────────

    [Fact]
    public void DrawImage_WithData_ProducesImageElement()
    {
        // Minimal PNG header
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var doc = Render(ctx => ctx.DrawImage(data, 10, 10, 100, 100));
        Assert.Equal(1, Count(doc, "image"));
        var img = doc.Descendants(Ns + "image").Single();
        Assert.StartsWith("data:image/png;base64,", img.Attribute("href")!.Value);
    }

    [Fact]
    public void DrawImage_JpegData_HasJpegMimeType()
    {
        byte[] data = [0xFF, 0xD8, 0xFF, 0xE0];
        var doc = Render(ctx => ctx.DrawImage(data, 0, 0, 50, 50));
        var img = doc.Descendants(Ns + "image").Single();
        Assert.StartsWith("data:image/jpeg;base64,", img.Attribute("href")!.Value);
    }

    [Fact]
    public void DrawImage_EmptyData_ProducesNoElement()
    {
        var doc = Render(ctx => ctx.DrawImage(ReadOnlySpan<byte>.Empty, 0, 0, 50, 50));
        Assert.Equal(0, Count(doc, "image"));
    }

    // ── MeasureText ───────────────────────────────────────────────────

    [Fact]
    public void MeasureText_NonEmpty_ReturnsPositiveDimensions()
    {
        var ctx = new SvgRenderContext(200, 200);
        var metrics = ctx.MeasureText("Hello", 14);
        Assert.True(metrics.Width > 0);
        Assert.True(metrics.Height > 0);
    }

    [Fact]
    public void MeasureText_Empty_ReturnsZero()
    {
        var ctx = new SvgRenderContext(200, 200);
        var metrics = ctx.MeasureText("", 14);
        Assert.Equal(0, metrics.Width);
        Assert.Equal(0, metrics.Height);
    }

    [Fact]
    public void MeasureText_LongerText_ReturnsLargerWidth()
    {
        var ctx = new SvgRenderContext(200, 200);
        var short_ = ctx.MeasureText("Hi", 14);
        var long_ = ctx.MeasureText("Hello World!", 14);
        Assert.True(long_.Width > short_.Width);
    }

    // ── SaveState / RestoreState ──────────────────────────────────────

    [Fact]
    public void SaveState_CreatesGroupElement()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
        });
        Assert.Equal(1, Count(doc, "g"));
        // Line should be inside the group
        var g = doc.Descendants(Ns + "g").Single();
        Assert.Single(g.Descendants(Ns + "line"));
    }

    [Fact]
    public void Translate_AddsTransformAttribute()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.Translate(50, 100);
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
        });
        var g = doc.Descendants(Ns + "g").Single();
        Assert.Contains("translate(50,100)", g.Attribute("transform")!.Value);
    }

    [Fact]
    public void Rotate_AddsTransformAttribute()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.Rotate(45);
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
        });
        var g = doc.Descendants(Ns + "g").Single();
        Assert.Contains("rotate(45)", g.Attribute("transform")!.Value);
    }

    [Fact]
    public void Scale_AddsTransformAttribute()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.Scale(2, 3);
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
        });
        var g = doc.Descendants(Ns + "g").Single();
        Assert.Contains("scale(2,3)", g.Attribute("transform")!.Value);
    }

    [Fact]
    public void MultipleTransforms_CombineOnGroup()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.Translate(10, 20);
            ctx.Rotate(90);
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
        });
        var g = doc.Descendants(Ns + "g").Single();
        var transform = g.Attribute("transform")!.Value;
        Assert.Contains("translate(10,20)", transform);
        Assert.Contains("rotate(90)", transform);
    }

    [Fact]
    public void NestedSaveRestore_CreatesNestedGroups()
    {
        var doc = Render(ctx =>
        {
            ctx.SaveState();
            ctx.Translate(10, 0);
            ctx.SaveState();
            ctx.Rotate(45);
            ctx.DrawLine(0, 0, 10, 10, ColorHelper.Black, 1);
            ctx.RestoreState();
            ctx.RestoreState();
        });
        // Outer group + inner group = 2 groups
        Assert.Equal(2, Count(doc, "g"));
    }

    // ── SetClipRect / ResetClip ───────────────────────────────────────

    [Fact]
    public void SetClipRect_CreatesClipPathInDefs()
    {
        var doc = Render(ctx =>
        {
            ctx.SetClipRect(10, 10, 100, 100);
            ctx.DrawLine(0, 0, 50, 50, ColorHelper.Black, 1);
            ctx.ResetClip();
        });
        var defs = doc.Descendants(Ns + "defs");
        Assert.NotEmpty(defs);
        var clipPath = doc.Descendants(Ns + "clipPath");
        Assert.NotEmpty(clipPath);
    }

    // ── WriteTo ───────────────────────────────────────────────────────

    [Fact]
    public void WriteTo_ProducesValidSvgWithXmlDeclaration()
    {
        var ctx = new SvgRenderContext(100, 100);
        ctx.Clear(ColorHelper.White);
        ctx.DrawLine(0, 0, 100, 100, ColorHelper.Red, 1);

        using var ms = new MemoryStream();
        ctx.WriteTo(ms);

        ms.Position = 0;
        var content = new StreamReader(ms).ReadToEnd();
        Assert.StartsWith("<?xml", content);
        Assert.Contains("<svg", content);
        Assert.Contains("xmlns=\"http://www.w3.org/2000/svg\"", content);
        Assert.Contains("<line", content);
    }

    // ── Color formatting ──────────────────────────────────────────────

    [Fact]
    public void Color_RgbFormat_IsCorrect()
    {
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, ColorHelper.FromRgb(100, 150, 200), 1));
        var line = doc.Descendants(Ns + "line").Single();
        Assert.Equal("rgb(100,150,200)", line.Attribute("stroke")!.Value);
    }

    [Fact]
    public void Color_Opacity_IsCorrect()
    {
        // Alpha = 128 -> opacity ~0.502
        var doc = Render(ctx => ctx.DrawLine(0, 0, 10, 10, 0x80FF0000, 1));
        var line = doc.Descendants(Ns + "line").Single();
        var opacity = double.Parse(line.Attribute("opacity")!.Value,
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(opacity, 0.49, 0.51);
    }
}
