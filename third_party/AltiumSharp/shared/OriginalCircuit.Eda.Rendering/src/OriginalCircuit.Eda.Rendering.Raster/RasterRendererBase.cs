using OriginalCircuit.Eda.Models;
using OriginalCircuit.Eda.Rendering;
using SkiaSharp;

namespace OriginalCircuit.Eda.Rendering.Raster;

/// <summary>
/// Abstract base class for renderers that produce raster (PNG) output via SkiaSharp.
/// Subclasses implement <see cref="RenderComponent"/> to draw format-specific primitives.
/// </summary>
public abstract class RasterRendererBase : IRenderer
{
    /// <inheritdoc />
    public ValueTask RenderAsync(
        IComponent component,
        Stream output,
        RenderOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(output);

        options ??= new RenderOptions();

        using var bitmap = new SKBitmap(options.Width, options.Height);
        using var canvas = new SKCanvas(bitmap);
        using var context = new SkiaRenderContext(canvas);

        context.Clear(ColorHelper.EdaColorToArgb(options.BackgroundColor));

        var transform = new CoordTransform
        {
            ScreenWidth = options.Width,
            ScreenHeight = options.Height,
            Scale = options.Scale,
        };

        if (options.AutoZoom)
        {
            transform.AutoZoom(component.Bounds);
        }

        RenderComponent(component, context, transform);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(output);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask RenderAsync(
        IComponent component,
        string path,
        RenderOptions? options = null,
        CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await RenderAsync(component, stream, options, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders the component's primitives onto the given render context.
    /// </summary>
    /// <param name="component">The component to render.</param>
    /// <param name="context">The render context to draw on.</param>
    /// <param name="transform">The coordinate transform for world-to-screen conversion.</param>
    protected abstract void RenderComponent(IComponent component, IRenderContext context, CoordTransform transform);
}
