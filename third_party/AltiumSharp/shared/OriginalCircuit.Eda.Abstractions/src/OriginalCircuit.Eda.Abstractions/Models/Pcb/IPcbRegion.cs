using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB region primitive defined by an arbitrary closed polygon outline.
/// </summary>
public interface IPcbRegion : IPrimitive
{
    /// <summary>
    /// The vertices defining the closed outline of the region.
    /// </summary>
    IReadOnlyList<CoordPoint> Outline { get; }

    /// <summary>
    /// The layer the region is on.
    /// </summary>
    int Layer { get; }
}
