using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic line primitive drawn between two endpoints.
/// </summary>
public interface ISchLine : IPrimitive
{
    /// <summary>
    /// The start point of the line.
    /// </summary>
    CoordPoint Start { get; set; }

    /// <summary>
    /// The end point of the line.
    /// </summary>
    CoordPoint End { get; set; }

    /// <summary>
    /// The color of the line.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The width (thickness) of the line.
    /// </summary>
    Coord Width { get; }

    /// <summary>
    /// The dash style of the line.
    /// </summary>
    LineStyle LineStyle { get; }
}
