namespace OriginalCircuit.Altium.Models.Sch;

/// <summary>
/// Represents a schematic sheet-template reference (record type 39). Identifies the .SchDot
/// template applied to the sheet (border, title block, default fonts).
/// </summary>
public sealed class SchTemplate
{
    private readonly List<object> _primitives = new();

    /// <summary>
    /// Path to the .SchDot template file applied to the sheet.
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the template is locked from selection.
    /// </summary>
    public bool IsNotAccessible { get; set; }

    /// <summary>
    /// Owner part ID (sheet-level records use -1).
    /// </summary>
    public int OwnerPartId { get; set; } = -1;

    /// <summary>
    /// Owner index linking this record to its parent (sheet-level records use -1).
    /// </summary>
    public int OwnerIndex { get; set; } = -1;

    /// <summary>
    /// Index of this record within the sheet.
    /// </summary>
    public int IndexInSheet { get; set; }

    /// <summary>
    /// Graphical primitives owned by this template record. They are kept separate from the sheet's
    /// normal top-level primitives so hidden template graphics do not render as real schematic content.
    /// </summary>
    public IReadOnlyList<object> Primitives => _primitives;

    /// <summary>
    /// Adds a primitive that is owned by this template record.
    /// </summary>
    public void AddPrimitive(object primitive) => _primitives.Add(primitive);
}
