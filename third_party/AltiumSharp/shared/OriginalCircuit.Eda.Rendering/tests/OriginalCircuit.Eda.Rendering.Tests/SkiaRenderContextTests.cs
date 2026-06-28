using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Rendering;
using OriginalCircuit.Eda.Rendering.Raster;
using SkiaSharp;

namespace OriginalCircuit.Eda.Rendering.Tests;

/// <summary>
/// Tests for SkiaRenderContext. Since we can't easily inspect SkiaSharp canvas output
/// at element level, we verify that all operations execute without exceptions and
/// produce visible changes on the bitmap.
/// </summary>
public sealed class SkiaRenderContextTests : IDisposable
{
    private readonly SKBitmap _bitmap;
    private readonly SKCanvas _canvas;
    private readonly SkiaRenderContext _ctx;

    public SkiaRenderContextTests()
    {
        _bitmap = new SKBitmap(200, 200);
        _canvas = new SKCanvas(_bitmap);
        _ctx = new SkiaRenderContext(_canvas);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _canvas.Dispose();
        _bitmap.Dispose();
    }

    private bool HasNonWhitePixels()
    {
        for (int y = 0; y < _bitmap.Height; y++)
            for (int x = 0; x < _bitmap.Width; x++)
            {
                var pixel = _bitmap.GetPixel(x, y);
                if (pixel != SKColors.White && pixel.Alpha > 0)
                    return true;
            }
        return false;
    }

    [Fact]
    public void Clear_FillsEntireBitmap()
    {
        _ctx.Clear(ColorHelper.Red);
        var pixel = _bitmap.GetPixel(100, 100);
        Assert.Equal(SKColors.Red, pixel);
    }

    [Fact]
    public void DrawLine_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawLine(0, 0, 199, 199, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawLine_AllStyles_DoNotThrow()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawLine(0, 0, 100, 0, ColorHelper.Black, 1, LineStyle.Solid);
        _ctx.DrawLine(0, 20, 100, 20, ColorHelper.Black, 1, LineStyle.Dash);
        _ctx.DrawLine(0, 40, 100, 40, ColorHelper.Black, 1, LineStyle.Dot);
        _ctx.DrawLine(0, 60, 100, 60, ColorHelper.Black, 1, LineStyle.DashDot);
        _ctx.DrawLine(0, 80, 100, 80, ColorHelper.Black, 1, LineStyle.DashDotDot);
    }

    [Fact]
    public void DrawRectangle_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawRectangle(10, 10, 100, 80, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void FillRectangle_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.FillRectangle(10, 10, 100, 80, ColorHelper.Blue);
        var pixel = _bitmap.GetPixel(50, 50);
        Assert.Equal(SKColors.Blue, pixel);
    }

    [Fact]
    public void DrawRoundedRectangle_DoesNotThrow()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawRoundedRectangle(10, 10, 100, 80, 8, 6, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void FillRoundedRectangle_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.FillRoundedRectangle(10, 10, 100, 80, 10, ColorHelper.Green);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawEllipse_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawEllipse(100, 100, 50, 30, ColorHelper.Red, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void FillEllipse_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.FillEllipse(100, 100, 50, 30, ColorHelper.Red);
        var pixel = _bitmap.GetPixel(100, 100);
        Assert.Equal(SKColors.Red, pixel);
    }

    [Fact]
    public void DrawArc_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawArc(100, 100, 50, 50, 0, 180, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawPolygon_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        double[] xs = [50, 150, 100];
        double[] ys = [20, 20, 180];
        _ctx.DrawPolygon(xs, ys, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void FillPolygon_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        double[] xs = [50, 150, 100];
        double[] ys = [20, 20, 180];
        _ctx.FillPolygon(xs, ys, ColorHelper.Red);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawPolygon_TooFewPoints_DoesNotThrow()
    {
        _ctx.Clear(ColorHelper.White);
        double[] xs = [50];
        double[] ys = [20];
        _ctx.DrawPolygon(xs, ys, ColorHelper.Black, 2); // Should be no-op
    }

    [Fact]
    public void FillPolygon_TooFewPoints_DoesNotThrow()
    {
        _ctx.Clear(ColorHelper.White);
        double[] xs = [50, 100];
        double[] ys = [20, 40];
        _ctx.FillPolygon(xs, ys, ColorHelper.Red); // Need 3+ for fill
    }

    [Fact]
    public void DrawPolyline_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        double[] xs = [10, 50, 100, 150, 190];
        double[] ys = [100, 20, 180, 20, 100];
        _ctx.DrawPolyline(xs, ys, ColorHelper.Blue, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawPolyline_TooFewPoints_DoesNotThrow()
    {
        double[] xs = [10];
        double[] ys = [10];
        _ctx.DrawPolyline(xs, ys, ColorHelper.Blue, 2);
    }

    [Fact]
    public void DrawBezier_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawBezier(10, 100, 50, 10, 150, 190, 190, 100, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void FillPie_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.FillPie(100, 100, 50, 50, 0, 90, ColorHelper.Green);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawPie_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawPie(100, 100, 50, 50, 0, 90, ColorHelper.Black, 2);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawText_SimpleOverload_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawText("Hello", 50, 100, 24, ColorHelper.Black);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawText_EmptyString_DoesNotThrow()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.DrawText("", 50, 100, 24, ColorHelper.Black);
    }

    [Fact]
    public void DrawText_WithOptions_ProducesVisibleOutput()
    {
        _ctx.Clear(ColorHelper.White);
        var opts = new TextRenderOptions
        {
            FontFamily = "Arial",
            Bold = true,
            Italic = true,
            HorizontalAlignment = TextHAlign.Center,
            VerticalAlignment = TextVAlign.Middle
        };
        _ctx.DrawText("Test", 100, 100, 20, ColorHelper.Black, opts);
        Assert.True(HasNonWhitePixels());
    }

    [Fact]
    public void DrawText_WithOptions_EmptyString_DoesNotThrow()
    {
        var opts = new TextRenderOptions();
        _ctx.DrawText("", 50, 50, 14, ColorHelper.Black, opts);
    }

    [Fact]
    public void MeasureText_NonEmpty_ReturnsPositiveDimensions()
    {
        var metrics = _ctx.MeasureText("Hello World", 14);
        Assert.True(metrics.Width > 0);
        Assert.True(metrics.Height > 0);
    }

    [Fact]
    public void MeasureText_Empty_ReturnsZero()
    {
        var metrics = _ctx.MeasureText("", 14);
        Assert.Equal(0, metrics.Width);
        Assert.Equal(0, metrics.Height);
    }

    [Fact]
    public void MeasureText_LongerText_ReturnsLargerWidth()
    {
        var short_ = _ctx.MeasureText("Hi", 14);
        var long_ = _ctx.MeasureText("Hello World!", 14);
        Assert.True(long_.Width > short_.Width);
    }

    [Fact]
    public void MeasureText_WithOptions_Works()
    {
        var opts = new TextRenderOptions { FontFamily = "Courier", Bold = true };
        var metrics = _ctx.MeasureText("Test", 14, opts);
        Assert.True(metrics.Width > 0);
        Assert.True(metrics.Height > 0);
    }

    [Fact]
    public void SaveState_RestoreState_DoNotThrow()
    {
        _ctx.SaveState();
        _ctx.Translate(10, 10);
        _ctx.Rotate(45);
        _ctx.Scale(2, 2);
        _ctx.RestoreState();
    }

    [Fact]
    public void RestoreState_WithoutSave_DoesNotThrow()
    {
        _ctx.RestoreState(); // Should not crash
    }

    [Fact]
    public void NestedSaveRestore_Works()
    {
        _ctx.SaveState();
        _ctx.Translate(10, 10);
        _ctx.SaveState();
        _ctx.Rotate(45);
        _ctx.RestoreState();
        _ctx.RestoreState();
    }

    [Fact]
    public void Translate_AffectsSubsequentDrawing()
    {
        _ctx.Clear(ColorHelper.White);
        _ctx.SaveState();
        _ctx.Translate(100, 100);
        _ctx.FillRectangle(0, 0, 10, 10, ColorHelper.Red);
        _ctx.RestoreState();

        // The fill should be at (100,100), not (0,0)
        var pixelAtOrigin = _bitmap.GetPixel(5, 5);
        var pixelAtTranslated = _bitmap.GetPixel(105, 105);
        Assert.Equal(SKColors.White, pixelAtOrigin);
        Assert.Equal(SKColors.Red, pixelAtTranslated);
    }

    [Fact]
    public void SetClipRect_ResetClip_DoNotThrow()
    {
        _ctx.SetClipRect(10, 10, 100, 100);
        _ctx.DrawLine(0, 0, 199, 199, ColorHelper.Black, 2);
        _ctx.ResetClip();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        using var bitmap = new SKBitmap(100, 100);
        using var canvas = new SKCanvas(bitmap);
        var ctx = new SkiaRenderContext(canvas);
        ctx.Dispose();
        ctx.Dispose(); // Should not throw
    }

    [Fact]
    public void Constructor_NullCanvas_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SkiaRenderContext(null!));
    }
}
