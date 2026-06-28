using OriginalCircuit.Eda.Models;
using OriginalCircuit.Eda.Rendering;

namespace OriginalCircuit.Eda.Rendering.Svg;

/// <summary>
/// Abstract base class for renderers that produce SVG vector output.
/// Subclasses implement <see cref="RenderComponent"/> to draw format-specific primitives.
/// </summary>
public abstract class SvgRendererBase : IRenderer
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

        var width = (double)options.Width;
        var height = (double)options.Height;

        var ctx = new SvgRenderContext(width, height);
        ctx.Clear(ColorHelper.EdaColorToArgb(options.BackgroundColor));

        var transform = new CoordTransform
        {
            ScreenWidth = width,
            ScreenHeight = height,
            Scale = options.Scale,
        };

        if (options.AutoZoom)
        {
            transform.AutoZoom(component.Bounds);
        }

        RenderComponent(component, ctx, transform);

        ctx.WriteTo(output);

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
