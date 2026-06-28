using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic wire primitive that carries an electrical signal between connection points.
/// </summary>
public interface ISchWire : IPrimitive
{
    /// <summary>
    /// The vertices defining the wire path.
    /// </summary>
    IReadOnlyList<CoordPoint> Vertices { get; }

    /// <summary>
    /// The color of the wire.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the wire.
    /// </summary>
    Coord LineWidth { get; }
}
