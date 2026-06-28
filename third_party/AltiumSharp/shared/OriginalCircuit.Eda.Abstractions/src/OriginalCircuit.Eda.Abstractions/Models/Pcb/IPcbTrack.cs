using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB track (trace) primitive that forms a copper connection between two points.
/// </summary>
public interface IPcbTrack : IPrimitive
{
    /// <summary>
    /// The start point of the track.
    /// </summary>
    CoordPoint Start { get; set; }

    /// <summary>
    /// The end point of the track.
    /// </summary>
    CoordPoint End { get; set; }

    /// <summary>
    /// The width of the track.
    /// </summary>
    Coord Width { get; set; }

    /// <summary>
    /// The layer the track is on.
    /// </summary>
    int Layer { get; }
}
