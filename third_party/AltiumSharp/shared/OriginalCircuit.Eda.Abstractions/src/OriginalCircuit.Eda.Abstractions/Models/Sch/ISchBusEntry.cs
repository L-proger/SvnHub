using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic bus entry primitive that connects a wire or net to a bus.
/// </summary>
public interface ISchBusEntry : IPrimitive
{
    /// <summary>
    /// The start location of the bus entry.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The end point (corner) of the bus entry.
    /// </summary>
    CoordPoint Corner { get; }

    /// <summary>
    /// The color of the bus entry.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the bus entry line.
    /// </summary>
    Coord LineWidth { get; }
}
