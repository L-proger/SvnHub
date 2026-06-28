namespace OriginalCircuit.Eda.Primitives;

/// <summary>
/// Represents a color with red, green, blue, and alpha channels.
/// This is the format-agnostic color type used across all EDA interfaces.
/// </summary>
/// <param name="R">The red channel (0-255).</param>
/// <param name="G">The green channel (0-255).</param>
/// <param name="B">The blue channel (0-255).</param>
/// <param name="A">The alpha channel (0-255), where 255 is fully opaque.</param>
public readonly record struct EdaColor(byte R, byte G, byte B, byte A)
{
    /// <summary>
    /// Fully transparent (alpha = 0).
    /// </summary>
    public static readonly EdaColor Transparent = new(0, 0, 0, 0);

    /// <summary>
    /// Opaque black.
    /// </summary>
    public static readonly EdaColor Black = new(0, 0, 0, 255);

    /// <summary>
    /// Opaque white.
    /// </summary>
    public static readonly EdaColor White = new(255, 255, 255, 255);

    /// <summary>
    /// Creates a color from alpha, red, green, and blue channels.
    /// </summary>
    /// <param name="a">The alpha channel (0-255).</param>
    /// <param name="r">The red channel (0-255).</param>
    /// <param name="g">The green channel (0-255).</param>
    /// <param name="b">The blue channel (0-255).</param>
    /// <returns>A new <see cref="EdaColor"/> with the specified components.</returns>
    public static EdaColor FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

    /// <summary>
    /// Creates a fully opaque color from red, green, and blue channels.
    /// </summary>
    /// <param name="r">The red channel (0-255).</param>
    /// <param name="g">The green channel (0-255).</param>
    /// <param name="b">The blue channel (0-255).</param>
    /// <returns>A new opaque <see cref="EdaColor"/> with the specified components.</returns>
    public static EdaColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);
}
