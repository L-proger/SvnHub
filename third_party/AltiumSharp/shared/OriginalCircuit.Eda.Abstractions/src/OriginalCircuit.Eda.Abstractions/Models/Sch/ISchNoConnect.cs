using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic no-connect marker that indicates a pin is intentionally unconnected.
/// </summary>
public interface ISchNoConnect : IPrimitive
{
    /// <summary>
    /// The location of the no-connect marker.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The color of the no-connect marker.
    /// </summary>
    EdaColor Color { get; }
}
