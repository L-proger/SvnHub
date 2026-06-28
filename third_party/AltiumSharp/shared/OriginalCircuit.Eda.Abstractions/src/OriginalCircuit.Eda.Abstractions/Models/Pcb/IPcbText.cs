using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB text string primitive placed on a copper or silkscreen layer.
/// </summary>
public interface IPcbText : IPrimitive
{
    /// <summary>
    /// The text content.
    /// </summary>
    string Text { get; set; }

    /// <summary>
    /// The anchor location of the text.
    /// </summary>
    CoordPoint Location { get; set; }

    /// <summary>
    /// The height of the text characters.
    /// </summary>
    Coord Height { get; }

    /// <summary>
    /// The stroke width used for text rendering.
    /// </summary>
    Coord StrokeWidth { get; }

    /// <summary>
    /// The rotation angle of the text in degrees.
    /// </summary>
    double Rotation { get; }

    /// <summary>
    /// The layer the text is on.
    /// </summary>
    int Layer { get; }

    /// <summary>
    /// Whether the text is mirrored.
    /// </summary>
    bool IsMirrored { get; }

    /// <summary>
    /// The font family name, or <see langword="null"/> for stroke font.
    /// </summary>
    string? FontName { get; }

    /// <summary>
    /// Whether the font is bold.
    /// </summary>
    bool FontBold { get; }

    /// <summary>
    /// Whether the font is italic.
    /// </summary>
    bool FontItalic { get; }
}
