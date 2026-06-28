using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic sheet symbol primitive that references a sub-sheet in a hierarchical design.
/// </summary>
public interface ISchSheet : IPrimitive
{
    /// <summary>
    /// The top-left location of the sheet symbol.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The size of the sheet symbol (width as X, height as Y).
    /// </summary>
    CoordPoint Size { get; }

    /// <summary>
    /// The display name of the sheet.
    /// </summary>
    string SheetName { get; }

    /// <summary>
    /// The file name of the referenced sub-sheet.
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// The sheet pins (connection points) on this sheet symbol.
    /// </summary>
    IReadOnlyList<ISchSheetPin> Pins { get; }

    /// <summary>
    /// The border color of the sheet symbol.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The fill color of the sheet symbol.
    /// </summary>
    EdaColor FillColor { get; }

    /// <summary>
    /// The width of the border line.
    /// </summary>
    Coord LineWidth { get; }
}
