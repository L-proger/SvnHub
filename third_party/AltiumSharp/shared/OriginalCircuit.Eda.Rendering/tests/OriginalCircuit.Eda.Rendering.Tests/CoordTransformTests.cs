using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Eda.Rendering.Tests;

public sealed class CoordTransformTests
{
    [Fact]
    public void AutoZoom_SetsPositiveScale()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600
        };

        var bounds = new CoordRect(
            Coord.FromMils(0), Coord.FromMils(0),
            Coord.FromMils(1000), Coord.FromMils(800));

        transform.AutoZoom(bounds);

        Assert.True(transform.Scale > 0);
    }

    [Fact]
    public void AutoZoom_CentersOnBounds()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600
        };

        var bounds = new CoordRect(
            Coord.FromMils(100), Coord.FromMils(200),
            Coord.FromMils(500), Coord.FromMils(400));

        transform.AutoZoom(bounds);

        var expectedCenterX = (Coord.FromMils(100).ToRaw() + Coord.FromMils(500).ToRaw()) / 2.0;
        var expectedCenterY = (Coord.FromMils(200).ToRaw() + Coord.FromMils(400).ToRaw()) / 2.0;
        Assert.InRange(transform.CenterX, expectedCenterX - 1, expectedCenterX + 1);
        Assert.InRange(transform.CenterY, expectedCenterY - 1, expectedCenterY + 1);
    }

    [Fact]
    public void AutoZoom_EmptyBounds_NoChange()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600,
            Scale = 1.0,
            CenterX = 42
        };

        var bounds = new CoordRect(Coord.Zero, Coord.Zero, Coord.Zero, Coord.Zero);
        transform.AutoZoom(bounds);

        Assert.Equal(42, transform.CenterX);
    }

    [Fact]
    public void AutoZoom_RespectsMarginParameter()
    {
        var transform = new CoordTransform { ScreenWidth = 100, ScreenHeight = 100 };
        var bounds = new CoordRect(
            Coord.FromMils(0), Coord.FromMils(0),
            Coord.FromMils(100), Coord.FromMils(100));

        transform.AutoZoom(bounds, margin: 0.5);
        var halfScale = transform.Scale;

        transform.AutoZoom(bounds, margin: 1.0);
        var fullScale = transform.Scale;

        Assert.True(fullScale > halfScale, "Larger margin should produce a larger scale");
    }

    [Fact]
    public void AutoZoom_WideBounds_ScalesBasedOnWidth()
    {
        var transform = new CoordTransform { ScreenWidth = 800, ScreenHeight = 600 };
        // Very wide, not tall — width should be the limiting dimension
        var bounds = new CoordRect(
            Coord.FromMils(0), Coord.FromMils(0),
            Coord.FromMils(10000), Coord.FromMils(100));

        transform.AutoZoom(bounds, margin: 1.0);

        // Scale should be based on width: 800 / (10000 * raw_per_mil)
        var expectedScale = 800.0 / (Coord.FromMils(10000).ToRaw());
        Assert.InRange(transform.Scale, expectedScale - 0.0001, expectedScale + 0.0001);
    }

    [Fact]
    public void AutoZoom_TallBounds_ScalesBasedOnHeight()
    {
        var transform = new CoordTransform { ScreenWidth = 800, ScreenHeight = 600 };
        // Very tall, not wide — height should be the limiting dimension
        var bounds = new CoordRect(
            Coord.FromMils(0), Coord.FromMils(0),
            Coord.FromMils(100), Coord.FromMils(10000));

        transform.AutoZoom(bounds, margin: 1.0);

        var expectedScale = 600.0 / (Coord.FromMils(10000).ToRaw());
        Assert.InRange(transform.Scale, expectedScale - 0.0001, expectedScale + 0.0001);
    }

    [Fact]
    public void WorldToScreen_OriginMapsToScreenCenter()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600,
            Scale = 0.01,
            CenterX = 0,
            CenterY = 0
        };

        var (sx, sy) = transform.WorldToScreen(Coord.FromMils(0), Coord.FromMils(0));

        Assert.InRange(sx, 399.9, 400.1);
        Assert.InRange(sy, 299.9, 300.1);
    }

    [Fact]
    public void WorldToScreen_InvertsY()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600,
            Scale = 0.01,
            CenterX = 0,
            CenterY = 0
        };

        var (_, sy1) = transform.WorldToScreen(Coord.FromMils(0), Coord.FromMils(100));
        var (_, sy2) = transform.WorldToScreen(Coord.FromMils(0), Coord.FromMils(-100));

        Assert.True(sy1 < sy2, "Positive Y in world should map to smaller screen Y (inverted)");
    }

    [Fact]
    public void WorldToScreen_CoordPointOverload_MatchesTwoArgVersion()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600,
            Scale = 0.01,
            CenterX = 100,
            CenterY = 200
        };

        var x = Coord.FromMils(50);
        var y = Coord.FromMils(75);
        var (sx1, sy1) = transform.WorldToScreen(x, y);
        var (sx2, sy2) = transform.WorldToScreen(new CoordPoint(x, y));

        Assert.Equal(sx1, sx2);
        Assert.Equal(sy1, sy2);
    }

    [Fact]
    public void WorldToScreen_PositiveXMovesRight()
    {
        var transform = new CoordTransform
        {
            ScreenWidth = 800,
            ScreenHeight = 600,
            Scale = 0.01,
            CenterX = 0,
            CenterY = 0
        };

        var (sx1, _) = transform.WorldToScreen(Coord.FromMils(0), Coord.FromMils(0));
        var (sx2, _) = transform.WorldToScreen(Coord.FromMils(100), Coord.FromMils(0));

        Assert.True(sx2 > sx1, "Positive X should map to larger screen X");
    }

    [Fact]
    public void ScaleValue_ScalesCorrectly()
    {
        var transform = new CoordTransform { Scale = 0.01 };
        var value = Coord.FromMils(100);
        var scaled = transform.ScaleValue(value);

        Assert.InRange(scaled, value.ToRaw() * 0.01 - 0.1, value.ToRaw() * 0.01 + 0.1);
    }

    [Fact]
    public void ScaleValue_ZeroScale_ReturnsZero()
    {
        var transform = new CoordTransform { Scale = 0.0 };
        var scaled = transform.ScaleValue(Coord.FromMils(500));

        Assert.Equal(0.0, scaled);
    }

    [Fact]
    public void DefaultProperties_HaveExpectedValues()
    {
        var transform = new CoordTransform();

        Assert.Equal(1.0, transform.Scale);
        Assert.Equal(0.0, transform.CenterX);
        Assert.Equal(0.0, transform.CenterY);
        Assert.Equal(0.0, transform.ScreenWidth);
        Assert.Equal(0.0, transform.ScreenHeight);
    }
}
