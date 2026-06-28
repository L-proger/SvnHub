using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic text label primitive used for annotation.
/// </summary>
public interface ISchLabel : IPrimitive
{
    /// <summary>
    /// The text content of the label.
    /// </summary>
    string Text { get; set; }

    /// <summary>
    /// The location of the label anchor point.
    /// </summary>
    CoordPoint Location { get; set; }

    /// <summary>
    /// The color of the label text.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The text justification of the label.
    /// </summary>
    TextJustification Justification { get; }

    /// <summary>
    /// The rotation of the label in degrees.
    /// </summary>
    double Rotation { get; }

    /// <summary>
    /// Whether the label text is mirrored.
    /// </summary>
    bool IsMirrored { get; }

    /// <summary>
    /// Whether the label is hidden (not rendered).
    /// </summary>
    bool IsHidden { get; }
}
