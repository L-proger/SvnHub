namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Shape of a PCB pad copper area.
/// </summary>
public enum PadShape
{
    /// <summary>Circular pad.</summary>
    Circle = 0,

    /// <summary>Rectangular pad.</summary>
    Rect = 1,

    /// <summary>Oval (oblong) pad.</summary>
    Oval = 2,

    /// <summary>Rounded rectangle pad.</summary>
    RoundRect = 3,

    /// <summary>Trapezoidal pad.</summary>
    Trapezoid = 4,

    /// <summary>Custom-shaped pad.</summary>
    Custom = 5
}
