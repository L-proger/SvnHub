using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic rectangle primitive defined by two corner points.
/// </summary>
public interface ISchRectangle : IPrimitive
{
    /// <summary>
    /// The first corner of the rectangle.
    /// </summary>
    CoordPoint Corner1 { get; set; }

    /// <summary>
    /// The second (opposite) corner of the rectangle.
    /// </summary>
    CoordPoint Corner2 { get; set; }

    /// <summary>
    /// The border color of the rectangle.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The fill color of the rectangle interior.
    /// </summary>
    EdaColor FillColor { get; }

    /// <summary>
    /// The width of the border line.
    /// </summary>
    Coord LineWidth { get; }

    /// <summary>
    /// Whether the rectangle interior is filled.
    /// </summary>
    bool IsFilled { get; }
}
