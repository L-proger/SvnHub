using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic Bezier curve primitive defined by a set of control points.
/// </summary>
public interface ISchBezier : IPrimitive
{
    /// <summary>
    /// The control points defining the Bezier curve.
    /// </summary>
    IReadOnlyList<CoordPoint> ControlPoints { get; }

    /// <summary>
    /// The color of the curve stroke.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the curve stroke.
    /// </summary>
    Coord LineWidth { get; }
}
