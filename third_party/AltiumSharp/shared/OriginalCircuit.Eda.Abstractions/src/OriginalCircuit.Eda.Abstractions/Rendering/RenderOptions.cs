using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Output image format for raster (bitmap) renderers. Ignored by vector (SVG) renderers.
/// </summary>
public enum RasterImageFormat
{
    /// <summary>PNG — lossless (the default).</summary>
    Png = 0,

    /// <summary>JPEG — lossy; honours <see cref="RenderOptions.Quality"/>.</summary>
    Jpeg = 1
}

/// <summary>
/// Options for rendering a component or document.
/// </summary>
public sealed record RenderOptions
{
    /// <summary>
    /// Output width in pixels (for raster) or units (for vector).
    /// </summary>
    public int Width { get; init; } = 1024;

    /// <summary>
    /// Output height in pixels (for raster) or units (for vector).
    /// </summary>
    public int Height { get; init; } = 768;

    /// <summary>
    /// Background color.
    /// </summary>
    public EdaColor BackgroundColor { get; init; } = EdaColor.White;

    /// <summary>
    /// Whether to automatically zoom to fit the component.
    /// </summary>
    public bool AutoZoom { get; init; } = true;

    /// <summary>
    /// Scale factor (1.0 = 100%).
    /// </summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>
    /// Raster output format (PNG or JPEG). Ignored by vector (SVG) renderers.
    /// </summary>
    public RasterImageFormat Format { get; init; } = RasterImageFormat.Png;

    /// <summary>
    /// Encoder quality (1-100) for lossy raster formats such as JPEG. Ignored for PNG.
    /// </summary>
    public int Quality { get; init; } = 100;
}
