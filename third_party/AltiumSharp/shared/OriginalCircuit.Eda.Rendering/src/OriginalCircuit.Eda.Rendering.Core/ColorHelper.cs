using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Utility methods for converting between <see cref="EdaColor"/> and packed ARGB integers.
/// </summary>
public static class ColorHelper
{
    /// <summary>
    /// Converts an <see cref="EdaColor"/> (R, G, B, A) to a packed ARGB uint (0xAARRGGBB).
    /// </summary>
    /// <param name="color">The color to convert.</param>
    /// <returns>The packed ARGB value.</returns>
    public static uint EdaColorToArgb(EdaColor color) =>
        ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

    /// <summary>
    /// Converts a packed ARGB uint (0xAARRGGBB) to an <see cref="EdaColor"/>.
    /// </summary>
    /// <param name="argb">The packed ARGB value.</param>
    /// <returns>The corresponding <see cref="EdaColor"/>.</returns>
    public static EdaColor ArgbToEdaColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));

    /// <summary>
    /// Returns <see langword="true"/> if the <see cref="EdaColor"/> has any non-zero RGB channel.
    /// </summary>
    /// <param name="color">The color to test.</param>
    /// <returns><see langword="true"/> if any RGB channel is non-zero.</returns>
    public static bool IsNonZero(EdaColor color) => color.R != 0 || color.G != 0 || color.B != 0;

    /// <summary>
    /// Creates a fully opaque ARGB color from individual red, green, and blue components.
    /// </summary>
    /// <param name="r">Red channel (0-255).</param>
    /// <param name="g">Green channel (0-255).</param>
    /// <param name="b">Blue channel (0-255).</param>
    /// <returns>The packed ARGB value with alpha set to 0xFF.</returns>
    public static uint FromRgb(byte r, byte g, byte b) =>
        0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;

    /// <summary>Black (0xFF000000).</summary>
    public const uint Black = 0xFF000000;

    /// <summary>White (0xFFFFFFFF).</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>Red (0xFFFF0000).</summary>
    public const uint Red = 0xFFFF0000;

    /// <summary>Green (0xFF00FF00).</summary>
    public const uint Green = 0xFF00FF00;

    /// <summary>Blue (0xFF0000FF).</summary>
    public const uint Blue = 0xFF0000FF;

    /// <summary>Yellow (0xFFFFFF00).</summary>
    public const uint Yellow = 0xFFFFFF00;

    /// <summary>Dark green (0xFF006400).</summary>
    public const uint DarkGreen = 0xFF006400;

    /// <summary>Gray (0xFF808080).</summary>
    public const uint Gray = 0xFF808080;
}
