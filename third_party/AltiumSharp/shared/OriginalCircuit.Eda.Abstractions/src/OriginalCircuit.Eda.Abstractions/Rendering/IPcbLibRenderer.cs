using OriginalCircuit.Eda.Models.Pcb;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Renderer specialized for PCB components.
/// </summary>
public interface IPcbLibRenderer : IRenderer
{
    /// <summary>
    /// Renders a PCB footprint component to a stream.
    /// </summary>
    /// <param name="component">The PCB component to render.</param>
    /// <param name="output">The output stream.</param>
    /// <param name="options">Optional render options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RenderAsync(
        IPcbComponent component,
        Stream output,
        RenderOptions? options = null,
        CancellationToken ct = default);
}
