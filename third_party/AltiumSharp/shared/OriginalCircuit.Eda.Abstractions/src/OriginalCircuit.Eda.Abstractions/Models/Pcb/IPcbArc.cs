using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB arc primitive defined by a center point, radius, and angular range.
/// </summary>
public interface IPcbArc : IPrimitive
{
    /// <summary>
    /// The center point of the arc.
    /// </summary>
    CoordPoint Center { get; set; }

    /// <summary>
    /// The radius of the arc.
    /// </summary>
    Coord Radius { get; set; }

    /// <summary>
    /// The start angle of the arc in degrees.
    /// </summary>
    double StartAngle { get; set; }

    /// <summary>
    /// The end angle of the arc in degrees.
    /// </summary>
    double EndAngle { get; set; }

    /// <summary>
    /// The width (thickness) of the arc stroke.
    /// </summary>
    Coord Width { get; }

    /// <summary>
    /// The layer the arc is on.
    /// </summary>
    int Layer { get; }
}
