using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic junction primitive that marks an electrical connection point between wires.
/// </summary>
public interface ISchJunction : IPrimitive
{
    /// <summary>
    /// The location of the junction.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The color of the junction marker.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The diameter of the junction marker.
    /// </summary>
    Coord Size { get; }
}
