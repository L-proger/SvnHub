using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Eda.Rendering.Tests;

public sealed class ColorHelperTests
{
    [Fact]
    public void EdaColorToArgb_OpaqueRed()
    {
        var color = new EdaColor(255, 0, 0, 255);
        var argb = ColorHelper.EdaColorToArgb(color);

        Assert.Equal(0xFFFF0000u, argb);
    }

    [Fact]
    public void EdaColorToArgb_OpaqueGreen()
    {
        var color = new EdaColor(0, 255, 0, 255);
        var argb = ColorHelper.EdaColorToArgb(color);

        Assert.Equal(0xFF00FF00u, argb);
    }

    [Fact]
    public void EdaColorToArgb_OpaqueBlue()
    {
        var color = new EdaColor(0, 0, 255, 255);
        var argb = ColorHelper.EdaColorToArgb(color);

        Assert.Equal(0xFF0000FFu, argb);
    }

    [Fact]
    public void EdaColorToArgb_SemiTransparent()
    {
        var color = new EdaColor(128, 64, 32, 200);
        var argb = ColorHelper.EdaColorToArgb(color);

        Assert.Equal((uint)200 << 24 | (uint)128 << 16 | (uint)64 << 8 | 32, argb);
    }

    [Fact]
    public void EdaColorToArgb_FullyTransparent()
    {
        var color = new EdaColor(0, 0, 0, 0);
        var argb = ColorHelper.EdaColorToArgb(color);

        Assert.Equal(0x00000000u, argb);
    }

    [Fact]
    public void ArgbToEdaColor_OpaqueWhite()
    {
        var color = ColorHelper.ArgbToEdaColor(0xFFFFFFFF);

        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
        Assert.Equal(255, color.A);
    }

    [Fact]
    public void ArgbToEdaColor_SemiTransparent()
    {
        var color = ColorHelper.ArgbToEdaColor(0x80FF8040);

        Assert.Equal(255, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(64, color.B);
        Assert.Equal(128, color.A);
    }

    [Fact]
    public void RoundTrip_EdaColorToArgb_ArgbToEdaColor()
    {
        var original = new EdaColor(123, 45, 67, 200);
        var argb = ColorHelper.EdaColorToArgb(original);
        var result = ColorHelper.ArgbToEdaColor(argb);

        Assert.Equal(original.R, result.R);
        Assert.Equal(original.G, result.G);
        Assert.Equal(original.B, result.B);
        Assert.Equal(original.A, result.A);
    }

    [Fact]
    public void IsNonZero_AllZero_ReturnsFalse()
    {
        Assert.False(ColorHelper.IsNonZero(new EdaColor(0, 0, 0, 255)));
    }

    [Fact]
    public void IsNonZero_RedNonZero_ReturnsTrue()
    {
        Assert.True(ColorHelper.IsNonZero(new EdaColor(1, 0, 0, 0)));
    }

    [Fact]
    public void IsNonZero_GreenNonZero_ReturnsTrue()
    {
        Assert.True(ColorHelper.IsNonZero(new EdaColor(0, 1, 0, 0)));
    }

    [Fact]
    public void IsNonZero_BlueNonZero_ReturnsTrue()
    {
        Assert.True(ColorHelper.IsNonZero(new EdaColor(0, 0, 1, 0)));
    }

    [Fact]
    public void FromRgb_CreatesOpaqueColor()
    {
        var argb = ColorHelper.FromRgb(100, 150, 200);

        Assert.Equal(0xFF000000u | (100u << 16) | (150u << 8) | 200u, argb);
        // Alpha should be FF
        Assert.Equal(0xFFu, (argb >> 24) & 0xFF);
    }

    [Fact]
    public void Constants_HaveCorrectValues()
    {
        Assert.Equal(0xFF000000u, ColorHelper.Black);
        Assert.Equal(0xFFFFFFFFu, ColorHelper.White);
        Assert.Equal(0xFFFF0000u, ColorHelper.Red);
        Assert.Equal(0xFF00FF00u, ColorHelper.Green);
        Assert.Equal(0xFF0000FFu, ColorHelper.Blue);
        Assert.Equal(0xFFFFFF00u, ColorHelper.Yellow);
        Assert.Equal(0xFF006400u, ColorHelper.DarkGreen);
        Assert.Equal(0xFF808080u, ColorHelper.Gray);
    }
}
