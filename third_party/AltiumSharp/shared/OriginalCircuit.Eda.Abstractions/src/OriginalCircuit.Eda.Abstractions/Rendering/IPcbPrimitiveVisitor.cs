using OriginalCircuit.Eda.Models.Pcb;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Visitor for PCB primitives, providing typed visit methods for each primitive type.
/// </summary>
/// <typeparam name="TContext">The rendering context type.</typeparam>
public interface IPcbPrimitiveVisitor<in TContext> : IPrimitiveVisitor<TContext>
{
    /// <summary>Visits a PCB pad.</summary>
    /// <param name="pad">The pad to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbPad pad, TContext context);

    /// <summary>Visits a PCB track.</summary>
    /// <param name="track">The track to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbTrack track, TContext context);

    /// <summary>Visits a PCB via.</summary>
    /// <param name="via">The via to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbVia via, TContext context);

    /// <summary>Visits a PCB arc.</summary>
    /// <param name="arc">The arc to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbArc arc, TContext context);

    /// <summary>Visits a PCB text.</summary>
    /// <param name="text">The text to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbText text, TContext context);

    /// <summary>Visits a PCB region.</summary>
    /// <param name="region">The region to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPcbRegion region, TContext context);
}
