using System.Globalization;
using System.Text;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;
using SkiaSharp;

namespace SvnHub.Web.Support;

internal static class AltiumPcbTextGeometryRenderer
{
    public static RenderedTextGeometry? Render(PcbText text, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        if (text.TextKind == PcbTextKind.BarCode)
        {
            return RenderBarcode(text);
        }

        if (text.IsTrueType || text.TextKind == PcbTextKind.TrueType || text.UseTTFonts)
        {
            return RenderTrueType(text, content);
        }

        return RenderStroke(text, content);
    }

    private static RenderedTextGeometry? RenderBarcode(PcbText text)
    {
        var layout = AltiumBarcodeTextGeometry.TryBuild(text);
        if (layout is null || layout.Foreground.Count == 0)
        {
            return null;
        }

        var path = new StringBuilder(layout.Foreground.Count * 80);
        foreach (var quad in layout.Foreground)
        {
            AppendPolygon(path, quad.Select(ToIbomTuple));
        }

        return path.Length == 0
            ? null
            : new RenderedTextGeometry(path.ToString(), null);
    }

    private static RenderedTextGeometry? RenderStroke(PcbText text, string content)
    {
        var height = Math.Max(text.Height.ToMils(), 1);
        var strokeWidth = Math.Max(text.StrokeWidth.ToMils(), height / 14.0);
        var style = ToStrokeStyle(text.StrokeFont);
        var segments = AltiumStrokeTextGeometry.Layout(content, style, out var advanceWidth);
        if (segments.Count == 0)
        {
            return null;
        }

        var frame = GetFrame(text);
        var localOffset = GetLocalOffset(text, content, advanceWidth, frame);
        var origin = ToIbomTuple(text.Location);
        var angle = -text.Rotation * Math.PI / 180.0;
        var mirrored = text.IsMirrored || text.MirrorFlag;
        var path = new StringBuilder(segments.Count * 44);

        if (frame.Inverted)
        {
            AppendTransformedRectangle(path, origin, angle, mirrored, 0, 0, frame.WidthMils, frame.HeightMils);
        }

        foreach (var segment in segments)
        {
            var x1 = (segment.X1 + localOffset.X) * height;
            var y1 = -(segment.Y1 + localOffset.Y) * height;
            var x2 = (segment.X2 + localOffset.X) * height;
            var y2 = -(segment.Y2 + localOffset.Y) * height;

            if (frame.Inverted)
            {
                AppendTransformedSegmentOutline(path, origin, angle, mirrored, x1, y1, x2, y2, strokeWidth);
                continue;
            }

            var start = Transform(origin, angle, mirrored, x1, y1);
            var end = Transform(origin, angle, mirrored, x2, y2);

            path.Append(CultureInfo.InvariantCulture, $"M {start.X:0.######} {start.Y:0.######} ");
            path.Append(CultureInfo.InvariantCulture, $"L {end.X:0.######} {end.Y:0.######} ");
        }

        return path.Length == 0
            ? null
            : new RenderedTextGeometry(path.ToString(), frame.Inverted ? null : Round(strokeWidth));
    }

    private static RenderedTextGeometry? RenderTrueType(PcbText text, string content)
    {
        var height = Math.Max(text.Height.ToMils(), 1);
        var fontSize = (float)Math.Max(height * 0.8951, 1);
        var typefaceStyle = text.FontBold && text.FontItalic
            ? SKFontStyle.BoldItalic
            : text.FontBold
                ? SKFontStyle.Bold
                : text.FontItalic
                    ? SKFontStyle.Italic
                    : SKFontStyle.Normal;

        using var typeface = SKTypeface.FromFamilyName(
            string.IsNullOrWhiteSpace(text.FontName) ? "Arial" : text.FontName,
            typefaceStyle);
        using var font = new SKFont(typeface, fontSize)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
            LinearMetrics = true,
            Subpixel = true,
        };
        using var paint = new SKPaint();
        using var localPath = new SKPath { FillType = SKPathFillType.EvenOdd };

        var lines = NormalizeLines(content);
        var lineSpacing = Math.Max(font.Spacing, fontSize * 1.2f);
        var maxAdvance = 0f;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Length == 0)
            {
                continue;
            }

            var glyphs = new ushort[font.CountGlyphs(line)];
            if (glyphs.Length == 0)
            {
                continue;
            }

            font.GetGlyphs(line, glyphs);
            var widths = new float[glyphs.Length];
            var bounds = new SKRect[glyphs.Length];
            font.GetGlyphWidths(glyphs, widths, bounds, paint);

            var cursor = 0f;
            var baseline = lineIndex * lineSpacing;
            for (var glyphIndex = 0; glyphIndex < glyphs.Length; glyphIndex++)
            {
                var glyphPath = font.GetGlyphPath(glyphs[glyphIndex]);
                if (glyphPath is not null)
                {
                    localPath.AddPath(glyphPath, cursor, baseline, SKPathAddMode.Append);
                    glyphPath.Dispose();
                }

                cursor += widths[glyphIndex];
            }

            maxAdvance = Math.Max(maxAdvance, cursor);
        }

        if (localPath.IsEmpty)
        {
            return null;
        }

        var frame = GetFrame(text);
        if (frame.Active)
        {
            return RenderFramedTrueType(text, content, frame, typefaceStyle);
        }

        var localOffset = GetLocalOffset(text, maxAdvance / fontSize, lines.Length, lineSpacing / fontSize, frame);
        using var translated = new SKPath();
        var translate = SKMatrix.CreateTranslation((float)(localOffset.X * fontSize), (float)(localOffset.Y * fontSize));
        localPath.Transform(translate, translated);

        using var transformed = new SKPath();
        var origin = ToIbomTuple(text.Location);
        var angle = -text.Rotation * Math.PI / 180.0;
        var mirror = text.IsMirrored || text.MirrorFlag ? -1.0 : 1.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var matrix = new SKMatrix(
            (float)(mirror * cos),
            (float)-sin,
            (float)origin.X,
            (float)(mirror * sin),
            (float)cos,
            (float)origin.Y,
            0,
            0,
            1);
        translated.Transform(matrix, transformed);

        var svgPath = frame.Inverted
            ? CreateTransformedRectanglePath(origin, angle, text.IsMirrored || text.MirrorFlag, 0, 0, frame.WidthMils, frame.HeightMils) + transformed.ToSvgPathData()
            : transformed.ToSvgPathData();
        return string.IsNullOrWhiteSpace(svgPath)
            ? null
            : new RenderedTextGeometry(svgPath, null);
    }

    private static RenderedTextGeometry? RenderFramedTrueType(
        PcbText text,
        string content,
        TextFrame frame,
        SKFontStyle typefaceStyle)
    {
        var lines = NormalizeLines(content);
        if (lines.Length == 0)
        {
            return null;
        }

        var textHeight = Math.Max(text.Height.ToMils(), 1);
        var (fontSize, lineSpacing) = GetFramedPcbTextMetrics(textHeight, frame, lines.Length);
        using var typeface = SKTypeface.FromFamilyName(
            string.IsNullOrWhiteSpace(text.FontName) ? "Arial" : text.FontName,
            typefaceStyle);
        using var font = new SKFont(typeface, (float)fontSize)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.None,
            LinearMetrics = true,
            Subpixel = true,
        };
        using var paint = new SKPaint();
        var horizontal = FrameHorizontal(text.InvertedRectJustification, text.Justification);
        var vertical = FrameVertical(text.InvertedRectJustification, text.Justification);
        var linePaths = new List<FramedTextLine>(lines.Length);

        try
        {
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Length == 0)
                {
                    continue;
                }

                var glyphs = new ushort[font.CountGlyphs(line)];
                if (glyphs.Length == 0)
                {
                    continue;
                }

                font.GetGlyphs(line, glyphs);
                var widths = new float[glyphs.Length];
                var bounds = new SKRect[glyphs.Length];
                font.GetGlyphWidths(glyphs, widths, bounds, paint);

                var linePath = new SKPath { FillType = SKPathFillType.EvenOdd };
                var cursor = 0f;
                for (var glyphIndex = 0; glyphIndex < glyphs.Length; glyphIndex++)
                {
                    var glyphPath = font.GetGlyphPath(glyphs[glyphIndex]);
                    if (glyphPath is not null)
                    {
                        linePath.AddPath(glyphPath, cursor, 0, SKPathAddMode.Append);
                        glyphPath.Dispose();
                    }

                    cursor += widths[glyphIndex];
                }

                if (linePath.IsEmpty)
                {
                    linePath.Dispose();
                    continue;
                }

                linePaths.Add(new FramedTextLine(linePath, cursor, lineIndex));
            }

            if (linePaths.Count == 0)
            {
                return null;
            }

            var overallMinY = linePaths.Select(line => line.Path.Bounds.Top + GetLineBaseline(line.LineIndex, lines.Length, lineSpacing)).Min();
            var overallMaxY = linePaths.Select(line => line.Path.Bounds.Bottom + GetLineBaseline(line.LineIndex, lines.Length, lineSpacing)).Max();
            var dy = vertical switch
            {
                2 => -frame.HeightMils - overallMinY,
                0 => -overallMaxY,
                _ => -frame.HeightMils / 2.0 - (overallMinY + overallMaxY) / 2.0,
            };

            using var localPath = new SKPath { FillType = SKPathFillType.EvenOdd };
            for (var lineIndex = 0; lineIndex < linePaths.Count; lineIndex++)
            {
                var line = linePaths[lineIndex];
                var bounds = line.Path.Bounds;
                var lineBoundsWidth = Math.Max(bounds.Right - bounds.Left, 0);
                var advanceWidth = line.Advance;
                if (advanceWidth <= 0 || (lineBoundsWidth > 0 && lineBoundsWidth > advanceWidth * 1.5))
                {
                    advanceWidth = 0;
                }

                var dx = advanceWidth > 0
                    ? horizontal switch
                    {
                        2 => frame.WidthMils - advanceWidth,
                        1 => (frame.WidthMils - advanceWidth) / 2.0,
                        _ => 0,
                    }
                    : horizontal switch
                    {
                        2 => frame.WidthMils - lineBoundsWidth - bounds.Left,
                        1 => (frame.WidthMils - lineBoundsWidth) / 2.0 - bounds.Left,
                        _ => -bounds.Left,
                };

                using var translatedLine = new SKPath();
                var baseline = GetLineBaseline(line.LineIndex, lines.Length, lineSpacing);
                line.Path.Transform(SKMatrix.CreateTranslation((float)dx, (float)(dy + baseline)), translatedLine);
                localPath.AddPath(translatedLine, SKPathAddMode.Append);
            }

            if (localPath.IsEmpty)
            {
                return null;
            }

            using var transformed = new SKPath();
            var origin = ToIbomTuple(text.Location);
            var angle = -text.Rotation * Math.PI / 180.0;
            var mirror = text.IsMirrored || text.MirrorFlag ? -1.0 : 1.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var matrix = new SKMatrix(
                (float)(mirror * cos),
                (float)-sin,
                (float)origin.X,
                (float)(mirror * sin),
                (float)cos,
                (float)origin.Y,
                0,
                0,
                1);
            localPath.Transform(matrix, transformed);

            var svgPath = frame.Inverted
                ? CreateTransformedRectanglePath(origin, angle, text.IsMirrored || text.MirrorFlag, 0, 0, frame.WidthMils, frame.HeightMils) + transformed.ToSvgPathData()
                : transformed.ToSvgPathData();
            return string.IsNullOrWhiteSpace(svgPath)
                ? null
                : new RenderedTextGeometry(svgPath, null);
        }
        finally
        {
            foreach (var line in linePaths)
            {
                line.Path.Dispose();
            }
        }
    }

    private sealed record FramedTextLine(SKPath Path, double Advance, int LineIndex);

    private static double GetLineBaseline(int lineIndex, int lineCount, double lineSpacing) =>
        -lineSpacing * (lineCount - 1 - lineIndex);

    private static (double FontSize, double LineSpacing) GetFramedPcbTextMetrics(
        double textHeight,
        TextFrame frame,
        int lineCount)
    {
        lineCount = Math.Max(lineCount, 1);

        // Match AltiumSharp's PCB realistic renderer for framed TrueType text.
        // The IPC export-oriented cap-height spacing is too tight for Altium text boxes.
        const double trueTypeEmScale = 0.8;
        var margin = Math.Min(frame.WidthMils, frame.HeightMils) * 0.07;
        var availableLineHeight = (frame.HeightMils - 2 * margin) / lineCount * 0.92;
        var fontSize = Math.Max(1, Math.Min(textHeight * trueTypeEmScale, availableLineHeight));
        return (fontSize, fontSize * 1.2);
    }

    private static (double X, double Y) GetLocalOffset(
        PcbText text,
        string content,
        float advanceWidth,
        TextFrame frame)
    {
        var lines = NormalizeLines(content);
        return GetLocalOffset(text, advanceWidth, lines.Length, 1.68, frame);
    }

    private static (double X, double Y) GetLocalOffset(
        PcbText text,
        double advanceWidth,
        int lineCount,
        double lineSpacing,
        TextFrame frame)
    {
        lineCount = Math.Max(lineCount, 1);
        var blockHeight = lineCount > 1 ? lineSpacing * lineCount : 1.0;

        if (frame.Active)
        {
            var boxWidth = frame.WidthMils / Math.Max(text.Height.ToMils(), 1);
            var boxHeight = frame.HeightMils / Math.Max(text.Height.ToMils(), 1);
            var horizontal = FrameHorizontal(text.InvertedRectJustification, text.Justification);
            var vertical = FrameVertical(text.InvertedRectJustification, text.Justification);

            var fx = horizontal switch
            {
                1 => (boxWidth - advanceWidth) / 2.0,
                2 => boxWidth - advanceWidth,
                _ => 0.0,
            };
            var fy = vertical switch
            {
                1 => (boxHeight - blockHeight) / 2.0,
                2 => boxHeight - blockHeight,
                _ => 0.0,
            };

            return (fx, fy);
        }

        // Match AltiumSharp/Altium PCB rendering: free PCB text is anchored at
        // bottom-left Location. Justification affects framed/multiline text only.
        return (0.0, 0.0);
    }

    private static TextFrame GetFrame(PcbText text)
    {
        var width = FirstPositive(text.InvertedRectWidth, text.InvRectWidth).ToMils();
        var height = FirstPositive(text.InvertedRectHeight, text.InvRectHeight).ToMils();
        var active = width > 0 && height > 0 && (text.IsFrame || text.UseInvertedRectangle || text.Inverted || text.IsInverted);
        var inverted = active && (text.UseInvertedRectangle || text.Inverted || text.IsInverted);
        return new TextFrame(active, inverted, Math.Max(width, 0), Math.Max(height, 0));
    }

    private static Coord FirstPositive(Coord first, Coord second) =>
        first > Coord.Zero ? first : second;

    private static int FrameHorizontal(PcbTextJustification frame, TextJustification fallback) => frame switch
    {
        PcbTextJustification.CenterTop or PcbTextJustification.CenterCenter or PcbTextJustification.CenterBottom => 1,
        PcbTextJustification.RightTop or PcbTextJustification.RightCenter or PcbTextJustification.RightBottom => 2,
        PcbTextJustification.LeftTop or PcbTextJustification.LeftCenter or PcbTextJustification.LeftBottom => 0,
        _ => fallback switch
        {
            TextJustification.BottomCenter or TextJustification.MiddleCenter or TextJustification.TopCenter => 1,
            TextJustification.BottomRight or TextJustification.MiddleRight or TextJustification.TopRight => 2,
            _ => 0,
        },
    };

    private static int FrameVertical(PcbTextJustification frame, TextJustification fallback) => frame switch
    {
        PcbTextJustification.LeftCenter or PcbTextJustification.CenterCenter or PcbTextJustification.RightCenter => 1,
        PcbTextJustification.LeftTop or PcbTextJustification.CenterTop or PcbTextJustification.RightTop => 2,
        PcbTextJustification.LeftBottom or PcbTextJustification.CenterBottom or PcbTextJustification.RightBottom => 0,
        _ => fallback switch
        {
            TextJustification.MiddleLeft or TextJustification.MiddleCenter or TextJustification.MiddleRight => 1,
            TextJustification.TopLeft or TextJustification.TopCenter or TextJustification.TopRight => 2,
            _ => 0,
        },
    };

    private static string[] NormalizeLines(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static AltiumStrokeTextGeometry.Style ToStrokeStyle(PcbStrokeFont font) => font switch
    {
        PcbStrokeFont.SansSerif => AltiumStrokeTextGeometry.Style.SansSerif,
        PcbStrokeFont.Serif => AltiumStrokeTextGeometry.Style.Serif,
        _ => AltiumStrokeTextGeometry.Style.Default,
    };

    private static (double X, double Y) Transform(
        (double X, double Y) origin,
        double angle,
        bool mirrored,
        double x,
        double y)
    {
        if (mirrored)
        {
            x = -x;
        }

        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        return (
            Round(origin.X + x * cos - y * sin),
            Round(origin.Y + x * sin + y * cos));
    }

    private static void AppendPolygon(StringBuilder path, IEnumerable<(double X, double Y)> points)
    {
        var array = points.ToArray();
        if (array.Length < 3)
        {
            return;
        }

        path.Append(CultureInfo.InvariantCulture, $"M {array[0].X:0.######} {array[0].Y:0.######} ");
        for (var i = 1; i < array.Length; i++)
        {
            path.Append(CultureInfo.InvariantCulture, $"L {array[i].X:0.######} {array[i].Y:0.######} ");
        }

        path.Append("Z ");
    }

    private static void AppendTransformedRectangle(
        StringBuilder path,
        (double X, double Y) origin,
        double angle,
        bool mirrored,
        double x,
        double y,
        double width,
        double height)
    {
        path.Append(CreateTransformedRectanglePath(origin, angle, mirrored, x, y, width, height));
    }

    private static string CreateTransformedRectanglePath(
        (double X, double Y) origin,
        double angle,
        bool mirrored,
        double x,
        double y,
        double width,
        double height)
    {
        var path = new StringBuilder(96);
        AppendPolygon(path, new[]
        {
            Transform(origin, angle, mirrored, x, y),
            Transform(origin, angle, mirrored, x + width, y),
            Transform(origin, angle, mirrored, x + width, y - height),
            Transform(origin, angle, mirrored, x, y - height),
        });
        return path.ToString();
    }

    private static void AppendTransformedSegmentOutline(
        StringBuilder path,
        (double X, double Y) origin,
        double angle,
        bool mirrored,
        double x1,
        double y1,
        double x2,
        double y2,
        double width)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.000001)
        {
            var r = width / 2.0;
            AppendPolygon(path, new[]
            {
                Transform(origin, angle, mirrored, x1 - r, y1 - r),
                Transform(origin, angle, mirrored, x1 + r, y1 - r),
                Transform(origin, angle, mirrored, x1 + r, y1 + r),
                Transform(origin, angle, mirrored, x1 - r, y1 + r),
            });
            return;
        }

        var nx = -dy / length * width / 2.0;
        var ny = dx / length * width / 2.0;
        AppendPolygon(path, new[]
        {
            Transform(origin, angle, mirrored, x1 + nx, y1 + ny),
            Transform(origin, angle, mirrored, x2 + nx, y2 + ny),
            Transform(origin, angle, mirrored, x2 - nx, y2 - ny),
            Transform(origin, angle, mirrored, x1 - nx, y1 - ny),
        });
    }

    private static (double X, double Y) ToIbomTuple(CoordPoint point) =>
        (Round(point.X.ToMils()), Round(-point.Y.ToMils()));

    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.AwayFromZero);
}

internal sealed record RenderedTextGeometry(string SvgPath, double? Thickness);

internal readonly record struct TextFrame(bool Active, bool Inverted, double WidthMils, double HeightMils);
