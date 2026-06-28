namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Type of a PCB pad, defining how it connects through the board.
/// </summary>
public enum PadType
{
    /// <summary>Through-hole pad with plated drill.</summary>
    ThruHole = 0,

    /// <summary>Surface-mount pad (no drill).</summary>
    Smd = 1,

    /// <summary>Non-plated through-hole (mechanical mounting).</summary>
    NpThruHole = 2,

    /// <summary>Connect pad (for internal plane connections).</summary>
    Connect = 3
}
