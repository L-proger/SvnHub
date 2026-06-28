namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Line dash style for rendering lines, polylines, and borders.
/// </summary>
public enum LineStyle
{
    /// <summary>Solid continuous line.</summary>
    Solid = 0,

    /// <summary>Dashed line.</summary>
    Dash = 1,

    /// <summary>Dotted line.</summary>
    Dot = 2,

    /// <summary>Alternating dash-dot pattern.</summary>
    DashDot = 3,

    /// <summary>Alternating dash-dot-dot pattern.</summary>
    DashDotDot = 4,

    /// <summary>Default style (format-defined).</summary>
    DefaultStyle = 5
}
