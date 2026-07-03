using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Altium.Rendering;

/// <summary>
/// Public facade for Altium PCB barcode text geometry.
/// </summary>
public static class AltiumBarcodeTextGeometry
{
    /// <summary>
    /// Built barcode foreground geometry in Altium world coordinates.
    /// </summary>
    public sealed record Layout(IReadOnlyList<IReadOnlyList<CoordPoint>> Foreground, bool Inverted);

    /// <summary>
    /// Builds barcode foreground quads for QR, Data Matrix, and supported 1-D barcode text.
    /// </summary>
    public static Layout? TryBuild(PcbText text)
    {
        var layout = PcbBarcodeGeometry.TryBuild(text);
        return layout is null
            ? null
            : new Layout(layout.Foreground, layout.Inverted);
    }
}
