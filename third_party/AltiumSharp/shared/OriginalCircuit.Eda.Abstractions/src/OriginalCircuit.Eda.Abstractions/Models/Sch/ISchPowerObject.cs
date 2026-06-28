using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic power port primitive that connects to a named power or ground net.
/// </summary>
public interface ISchPowerObject : IPrimitive
{
    /// <summary>
    /// The location of the power port.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The net name text of the power port.
    /// </summary>
    string? Text { get; }

    /// <summary>
    /// The visual style of the power port symbol.
    /// </summary>
    PowerPortStyle Style { get; }

    /// <summary>
    /// The rotation of the power port in degrees.
    /// </summary>
    double Rotation { get; }

    /// <summary>
    /// Whether the net name is displayed.
    /// </summary>
    bool ShowNetName { get; }

    /// <summary>
    /// The color of the power port symbol.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// Whether the power port symbol is mirrored.
    /// </summary>
    bool IsMirrored { get; }
}
