namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Visual style of a power port symbol on a schematic.
/// </summary>
public enum PowerPortStyle
{
    /// <summary>Circle symbol.</summary>
    Circle = 0,

    /// <summary>Arrow symbol.</summary>
    Arrow = 1,

    /// <summary>Bar (line) symbol.</summary>
    Bar = 2,

    /// <summary>Wave symbol.</summary>
    Wave = 3,

    /// <summary>Power ground symbol (multiple horizontal lines).</summary>
    PowerGround = 4,

    /// <summary>Signal ground symbol (triangle).</summary>
    SignalGround = 5,

    /// <summary>Earth ground symbol (lines with decreasing length).</summary>
    Earth = 6,

    /// <summary>GOST-style arrow symbol.</summary>
    GostArrow = 7,

    /// <summary>GOST-style power ground symbol.</summary>
    GostPowerGround = 8,

    /// <summary>GOST-style earth ground symbol.</summary>
    GostEarth = 9,

    /// <summary>GOST-style bar symbol.</summary>
    GostBar = 10
}
