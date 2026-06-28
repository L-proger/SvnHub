using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents an embedded image primitive on a schematic sheet.
/// </summary>
public interface ISchImage : IPrimitive
{
    /// <summary>
    /// The first corner of the image bounding rectangle.
    /// </summary>
    CoordPoint Corner1 { get; }

    /// <summary>
    /// The second (opposite) corner of the image bounding rectangle.
    /// </summary>
    CoordPoint Corner2 { get; }

    /// <summary>
    /// The raw image data bytes, or <see langword="null"/> if not loaded.
    /// </summary>
    byte[]? ImageData { get; }
}
