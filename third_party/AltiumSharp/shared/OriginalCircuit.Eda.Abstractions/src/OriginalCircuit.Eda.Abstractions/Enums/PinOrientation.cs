namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Orientation of a schematic pin relative to the component body.
/// </summary>
public enum PinOrientation
{
    /// <summary>Pin points to the right (0 degrees).</summary>
    Right = 0,

    /// <summary>Pin points upward (90 degrees).</summary>
    Up = 1,

    /// <summary>Pin points to the left (180 degrees).</summary>
    Left = 2,

    /// <summary>Pin points downward (270 degrees).</summary>
    Down = 3
}
