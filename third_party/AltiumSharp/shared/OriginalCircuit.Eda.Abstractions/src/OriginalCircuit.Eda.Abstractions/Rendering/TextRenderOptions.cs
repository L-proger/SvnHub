namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Horizontal text alignment.
/// </summary>
public enum TextHAlign
{
    /// <summary>Align text to the left.</summary>
    Left,

    /// <summary>Center text horizontally.</summary>
    Center,

    /// <summary>Align text to the right.</summary>
    Right
}

/// <summary>
/// Vertical text alignment.
/// </summary>
public enum TextVAlign
{
    /// <summary>Align text to the top.</summary>
    Top,

    /// <summary>Center text vertically.</summary>
    Middle,

    /// <summary>Align text to the bottom.</summary>
    Bottom,

    /// <summary>Align text to the baseline.</summary>
    Baseline
}

/// <summary>
/// Options for text rendering including font style and alignment.
/// </summary>
public sealed record TextRenderOptions
{
    /// <summary>Font family name (e.g. "Arial").</summary>
    public string FontFamily { get; init; } = "Arial";

    /// <summary>Whether the font is bold.</summary>
    public bool Bold { get; init; }

    /// <summary>Whether the font is italic.</summary>
    public bool Italic { get; init; }

    /// <summary>Horizontal text alignment relative to the anchor point.</summary>
    public TextHAlign HorizontalAlignment { get; init; } = TextHAlign.Left;

    /// <summary>Vertical text alignment relative to the anchor point.</summary>
    public TextVAlign VerticalAlignment { get; init; } = TextVAlign.Baseline;
}
