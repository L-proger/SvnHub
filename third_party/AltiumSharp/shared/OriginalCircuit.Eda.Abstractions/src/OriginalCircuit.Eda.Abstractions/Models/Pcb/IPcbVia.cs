using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB via primitive that provides a copper connection between layers.
/// </summary>
public interface IPcbVia : IPrimitive
{
    /// <summary>
    /// The center location of the via.
    /// </summary>
    CoordPoint Location { get; set; }

    /// <summary>
    /// The outer diameter of the via annular ring.
    /// </summary>
    Coord Diameter { get; set; }

    /// <summary>
    /// The drill hole diameter.
    /// </summary>
    Coord HoleSize { get; set; }

    /// <summary>
    /// The starting (top) layer of the via.
    /// </summary>
    int StartLayer { get; }

    /// <summary>
    /// The ending (bottom) layer of the via.
    /// </summary>
    int EndLayer { get; }
}
