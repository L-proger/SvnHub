using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic bus primitive that carries multiple signals as a single grouped line.
/// </summary>
public interface ISchBus : IPrimitive
{
    /// <summary>
    /// The vertices defining the bus path.
    /// </summary>
    IReadOnlyList<CoordPoint> Vertices { get; }

    /// <summary>
    /// The color of the bus.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the bus line.
    /// </summary>
    Coord LineWidth { get; }
}
