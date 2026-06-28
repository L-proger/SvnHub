using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic circle primitive defined by a center point and radius.
/// </summary>
public interface ISchCircle : IPrimitive
{
    /// <summary>
    /// The center point of the circle.
    /// </summary>
    CoordPoint Center { get; }

    /// <summary>
    /// The radius of the circle.
    /// </summary>
    Coord Radius { get; }

    /// <summary>
    /// The color of the circle stroke.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The fill color of the circle interior.
    /// </summary>
    EdaColor FillColor { get; }

    /// <summary>
    /// The width (thickness) of the circle stroke.
    /// </summary>
    Coord LineWidth { get; }

    /// <summary>
    /// Whether the circle interior is filled.
    /// </summary>
    bool IsFilled { get; }
}
