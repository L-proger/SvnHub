namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Fill type for schematic graphical shapes.
/// </summary>
public enum SchFillType
{
    /// <summary>No fill (transparent interior).</summary>
    None = 0,

    /// <summary>Filled with the specified fill color.</summary>
    Filled = 1,

    /// <summary>Filled with the background color.</summary>
    Background = 2
}
