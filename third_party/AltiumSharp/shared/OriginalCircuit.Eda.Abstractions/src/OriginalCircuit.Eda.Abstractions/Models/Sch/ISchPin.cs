using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic pin primitive that defines an electrical connection point on a component symbol.
/// </summary>
public interface ISchPin : IPrimitive
{
    /// <summary>
    /// The display name of the pin.
    /// </summary>
    string? Name { get; set; }

    /// <summary>
    /// The pin designator (e.g., pin number or identifier).
    /// </summary>
    string? Designator { get; set; }

    /// <summary>
    /// The connection point location of the pin.
    /// </summary>
    CoordPoint Location { get; set; }

    /// <summary>
    /// The graphical length of the pin stub.
    /// </summary>
    Coord Length { get; set; }

    /// <summary>
    /// The orientation of the pin relative to the component body.
    /// </summary>
    PinOrientation Orientation { get; }

    /// <summary>
    /// The electrical type of the pin, used for electrical rule checks.
    /// </summary>
    PinElectricalType ElectricalType { get; }

    /// <summary>
    /// Whether the pin name is visible.
    /// </summary>
    bool ShowName { get; }

    /// <summary>
    /// Whether the pin designator is visible.
    /// </summary>
    bool ShowDesignator { get; }

    /// <summary>
    /// Whether the pin is hidden (not rendered but still electrically connected).
    /// </summary>
    bool IsHidden { get; }
}
