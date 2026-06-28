using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic polyline primitive defined by a series of connected line segments.
/// </summary>
public interface ISchPolyline : IPrimitive
{
    /// <summary>
    /// The vertices of the polyline.
    /// </summary>
    IReadOnlyList<CoordPoint> Vertices { get; }

    /// <summary>
    /// The color of the polyline stroke.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the polyline stroke.
    /// </summary>
    Coord LineWidth { get; }

    /// <summary>
    /// The dash style of the polyline.
    /// </summary>
    LineStyle LineStyle { get; }
}
