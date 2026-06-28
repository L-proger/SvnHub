namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Electrical type of a schematic pin, defining its electrical behavior for ERC checks.
/// </summary>
public enum PinElectricalType
{
    /// <summary>Input pin.</summary>
    Input = 0,

    /// <summary>Output pin.</summary>
    Output = 1,

    /// <summary>Bidirectional (input/output) pin.</summary>
    Bidirectional = 2,

    /// <summary>Passive pin (resistor, capacitor, etc.).</summary>
    Passive = 3,

    /// <summary>Tri-state output pin.</summary>
    TriState = 4,

    /// <summary>Power input pin (e.g., VCC, GND on a logic gate).</summary>
    PowerIn = 5,

    /// <summary>Power output pin (e.g., voltage regulator output).</summary>
    PowerOut = 6,

    /// <summary>Open collector output pin.</summary>
    OpenCollector = 7,

    /// <summary>Open emitter output pin.</summary>
    OpenEmitter = 8,

    /// <summary>Unspecified electrical type.</summary>
    Unspecified = 9,

    /// <summary>No-connect pin (explicitly unconnected).</summary>
    NoConnect = 10,

    /// <summary>Free pin (can connect to anything).</summary>
    Free = 11
}
