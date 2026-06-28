using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a connection point (pin) on a schematic sheet symbol.
/// </summary>
public interface ISchSheetPin : IPrimitive
{
    /// <summary>
    /// The name of the sheet pin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The I/O type of the sheet pin (input, output, bidirectional, etc.).
    /// </summary>
    int IoType { get; }

    /// <summary>
    /// The side of the sheet symbol this pin is on (0=left, 1=right, 2=top, 3=bottom).
    /// </summary>
    int Side { get; }

    /// <summary>
    /// The location of the sheet pin.
    /// </summary>
    CoordPoint Location { get; }
}
