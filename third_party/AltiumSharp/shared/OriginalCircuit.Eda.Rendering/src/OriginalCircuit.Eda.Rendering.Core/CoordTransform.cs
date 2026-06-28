using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Transforms coordinates between EDA world space and screen/pixel space.
/// Works with <see cref="Coord"/>/<see cref="CoordPoint"/>/<see cref="CoordRect"/> from Eda.Abstractions.
/// </summary>
public sealed class CoordTransform
{
    /// <summary>
    /// Zoom scale factor applied when converting world units to screen pixels.
    /// </summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// X coordinate of the world-space center point, in raw internal units.
    /// </summary>
    public double CenterX { get; set; }

    /// <summary>
    /// Y coordinate of the world-space center point, in raw internal units.
    /// </summary>
    public double CenterY { get; set; }

    /// <summary>
    /// Width of the target screen or image in pixels.
    /// </summary>
    public double ScreenWidth { get; set; }

    /// <summary>
    /// Height of the target screen or image in pixels.
    /// </summary>
    public double ScreenHeight { get; set; }

    /// <summary>
    /// Converts world coordinates to screen pixel coordinates, applying scale, centering, and Y-axis inversion.
    /// </summary>
    /// <param name="worldX">The world-space X coordinate.</param>
    /// <param name="worldY">The world-space Y coordinate.</param>
    /// <returns>A tuple of (screenX, screenY) in pixel coordinates.</returns>
    public (double x, double y) WorldToScreen(Coord worldX, Coord worldY)
    {
        var sx = (worldX.ToRaw() - CenterX) * Scale + ScreenWidth / 2.0;
        var sy = (CenterY - worldY.ToRaw()) * Scale + ScreenHeight / 2.0; // Y inverted
        return (sx, sy);
    }

    /// <summary>
    /// Converts a <see cref="CoordPoint"/> to screen pixel coordinates.
    /// </summary>
    /// <param name="point">The world-space point.</param>
    /// <returns>A tuple of (screenX, screenY) in pixel coordinates.</returns>
    public (double x, double y) WorldToScreen(CoordPoint point) =>
        WorldToScreen(point.X, point.Y);

    /// <summary>
    /// Scales a <see cref="Coord"/> value from world units to screen pixel length.
    /// </summary>
    /// <param name="value">The world-space coordinate value.</param>
    /// <returns>The length in screen pixels.</returns>
    public double ScaleValue(Coord value) => value.ToRaw() * Scale;

    /// <summary>
    /// Computes <see cref="Scale"/>, <see cref="CenterX"/>, and <see cref="CenterY"/> so that
    /// the given bounding rectangle fits within the screen dimensions.
    /// </summary>
    /// <param name="bounds">The world-space bounding rectangle to fit.</param>
    /// <param name="margin">Fraction of the screen area to use (default 0.95 leaves a 5% margin).</param>
    public void AutoZoom(CoordRect bounds, double margin = 0.95)
    {
        if (bounds.Width.ToRaw() == 0 && bounds.Height.ToRaw() == 0) return;

        CenterX = (bounds.Min.X.ToRaw() + bounds.Max.X.ToRaw()) / 2.0;
        CenterY = (bounds.Min.Y.ToRaw() + bounds.Max.Y.ToRaw()) / 2.0;

        var scaleX = bounds.Width.ToRaw() > 0 ? ScreenWidth / (double)bounds.Width.ToRaw() : 1.0;
        var scaleY = bounds.Height.ToRaw() > 0 ? ScreenHeight / (double)bounds.Height.ToRaw() : 1.0;
        Scale = Math.Min(scaleX, scaleY) * margin;
    }
}
