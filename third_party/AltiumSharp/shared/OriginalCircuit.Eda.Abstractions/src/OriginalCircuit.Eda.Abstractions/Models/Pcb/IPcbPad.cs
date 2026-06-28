using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB pad primitive used for component soldering and through-hole or surface-mount connections.
/// </summary>
public interface IPcbPad : IPrimitive
{
    /// <summary>
    /// The pad designator (e.g., "1", "A1").
    /// </summary>
    string? Designator { get; set; }

    /// <summary>
    /// The center location of the pad.
    /// </summary>
    CoordPoint Location { get; set; }

    /// <summary>
    /// The rotation angle of the pad in degrees.
    /// </summary>
    double Rotation { get; }

    /// <summary>
    /// The copper shape of the pad.
    /// </summary>
    PadShape Shape { get; }

    /// <summary>
    /// The size of the pad copper area (width as X, height as Y).
    /// </summary>
    CoordPoint Size { get; }

    /// <summary>
    /// The drill hole diameter.
    /// </summary>
    Coord HoleSize { get; }

    /// <summary>
    /// The shape of the drill hole.
    /// </summary>
    PadHoleType HoleType { get; }

    /// <summary>
    /// Whether the drill hole is plated.
    /// </summary>
    bool IsPlated { get; }

    /// <summary>
    /// The layer the pad is on.
    /// </summary>
    int Layer { get; }

    /// <summary>
    /// The solder mask expansion (clearance from pad edge to mask opening).
    /// </summary>
    Coord SolderMaskExpansion { get; }

    /// <summary>
    /// The corner radius percentage for rounded rectangle pads (0-100).
    /// </summary>
    int CornerRadiusPercentage { get; }
}
