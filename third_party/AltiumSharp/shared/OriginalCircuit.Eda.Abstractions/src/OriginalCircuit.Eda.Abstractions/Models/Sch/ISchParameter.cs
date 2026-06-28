using OriginalCircuit.Eda.Enums;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic parameter primitive that stores a name-value pair on a component or sheet.
/// </summary>
public interface ISchParameter : IPrimitive
{
    /// <summary>
    /// The location of the parameter text.
    /// </summary>
    CoordPoint Location { get; }

    /// <summary>
    /// The parameter name (key).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The parameter value.
    /// </summary>
    string Value { get; }

    /// <summary>
    /// The color of the parameter text.
    /// </summary>
    EdaColor Color { get; }

    /// <summary>
    /// The orientation of the parameter text in degrees (0, 90, 180, 270).
    /// </summary>
    int Orientation { get; }

    /// <summary>
    /// The text justification of the parameter.
    /// </summary>
    TextJustification Justification { get; }

    /// <summary>
    /// Whether the parameter text is visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Whether the parameter text is mirrored.
    /// </summary>
    bool IsMirrored { get; }
}
