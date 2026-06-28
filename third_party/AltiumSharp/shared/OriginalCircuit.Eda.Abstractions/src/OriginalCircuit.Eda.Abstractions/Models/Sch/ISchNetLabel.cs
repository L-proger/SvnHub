using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic net label primitive that assigns a net name to a wire or bus.
/// </summary>
public interface ISchNetLabel : IPrimitive
{
    /// <summary>
    /// The location of the net label.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The net name text.
    /// </summary>
    string Text { get; }

    /// <summary>
    /// The color of the net label text.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The orientation of the net label in degrees (0, 90, 180, 270).
    /// </summary>
    int Orientation { get; }

    /// <summary>
    /// The text justification of the net label.
    /// </summary>
    TextJustification Justification { get; }
}
