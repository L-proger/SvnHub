using OriginalCircuit.Eda.Models;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Base interface for component renderers.
/// </summary>
public interface IRenderer
{
    /// <summary>
    /// Renders a component to a stream.
    /// </summary>
    /// <param name="component">The component to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="options">Optional render options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RenderAsync(
        IComponent component,
        Stream output,
        RenderOptions? options = null,
        CancellationToken ct = default);

    /// <summary>
    /// Renders a component to a file.
    /// </summary>
    /// <param name="component">The component to render.</param>
    /// <param name="path">The output file path.</param>
    /// <param name="options">Optional render options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RenderAsync(
        IComponent component,
        string path,
        RenderOptions? options = null,
        CancellationToken ct = default);
}
