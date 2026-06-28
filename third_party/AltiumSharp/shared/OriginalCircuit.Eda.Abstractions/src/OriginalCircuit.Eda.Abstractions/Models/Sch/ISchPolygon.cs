using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic polygon primitive defined by a closed set of vertices.
/// </summary>
public interface ISchPolygon : IPrimitive
{
    /// <summary>
    /// The vertices of the polygon.
    /// </summary>
    IReadOnlyList<CoordPoint> Vertices { get; }

    /// <summary>
    /// The border color of the polygon.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The fill color of the polygon interior.
    /// </summary>
    EdaColor FillColor { get; }

    /// <summary>
    /// The width (thickness) of the polygon border.
    /// </summary>
    Coord LineWidth { get; }

    /// <summary>
    /// Whether the polygon interior is filled.
    /// </summary>
    bool IsFilled { get; }
}
