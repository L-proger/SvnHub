using System.Collections.Generic;
using System.Linq;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Public facade for Altium's built-in PCB stroke-font geometry.
/// </summary>
public static class AltiumStrokeTextGeometry
{
    /// <summary>
    /// A stroke segment in normalized Altium glyph space, where text height is 1.0
    /// and +Y points upward from the baseline.
    /// </summary>
    public readonly record struct Segment(float X1, float Y1, float X2, float Y2);

    /// <summary>
    /// Built-in Altium stroke font style.
    /// </summary>
    public enum Style
    {
        Default = 1,
        SansSerif = 2,
        Serif = 3,
    }

    /// <summary>
    /// Lays out text into normalized stroke segments and returns the widest line advance.
    /// </summary>
    public static IReadOnlyList<Segment> Layout(string text, Style style, out float advanceWidth)
    {
        var segments = AltiumStrokeFont.Layout(text, ToInternalStyle(style), out advanceWidth);
        return segments
            .Select(s => new Segment(s.X1, s.Y1, s.X2, s.Y2))
            .ToArray();
    }

    /// <summary>
    /// Measures the widest line advance in normalized glyph units.
    /// </summary>
    public static float MeasureWidth(string text, Style style) =>
        AltiumStrokeFont.MeasureWidth(text, ToInternalStyle(style));

    private static AltiumStrokeFont.Style ToInternalStyle(Style style) => style switch
    {
        Style.SansSerif => AltiumStrokeFont.Style.SansSerif,
        Style.Serif => AltiumStrokeFont.Style.Serif,
        _ => AltiumStrokeFont.Style.Default,
    };
}
