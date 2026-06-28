namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Kind of PCB text rendering.
/// </summary>
public enum PcbTextKind
{
    /// <summary>Stroke (vector) font text.</summary>
    Stroke = 0,

    /// <summary>TrueType font text.</summary>
    TrueType = 1,

    /// <summary>Barcode text.</summary>
    BarCode = 2
}
