using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic arc primitive defined by a center point, radius, and angular range.
/// </summary>
public interface ISchArc : IPrimitive
{
    /// <summary>
    /// The center point of the arc.
    /// </summary>
    CoordPoint Center { get; }

    /// <summary>
    /// The radius of the arc.
    /// </summary>
    Coord Radius { get; }

    /// <summary>
    /// The start angle of the arc in degrees.
    /// </summary>
    double StartAngle { get; }

    /// <summary>
    /// The end angle of the arc in degrees.
    /// </summary>
    double EndAngle { get; }

    /// <summary>
    /// The color of the arc stroke.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the arc stroke.
    /// </summary>
    Coord LineWidth { get; }
}
