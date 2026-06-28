using OriginalCircuit.Eda.Models.Sch;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Renderer specialized for schematic components.
/// </summary>
public interface ISchLibRenderer : IRenderer
{
    /// <summary>
    /// Renders a schematic component to a stream.
    /// </summary>
    /// <param name="component">The schematic component to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="options">Optional render options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RenderAsync(
        ISchComponent component,
        Stream output,
        RenderOptions? options = null,
        CancellationToken ct = default);
}
