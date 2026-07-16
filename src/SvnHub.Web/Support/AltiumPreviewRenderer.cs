using System.Text;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Altium.Rendering.Svg;
using OriginalCircuit.Altium.Serialization.Readers;
using OriginalCircuit.Eda.Rendering;

namespace SvnHub.Web.Support;

public sealed class AltiumPreviewRenderer
{
    private const int DefaultWidth = 1600;
    private const int DefaultHeight = 1200;
    private readonly AltiumSvgFontEmbedder _fontEmbedder;

    public AltiumPreviewRenderer(AltiumSvgFontEmbedder fontEmbedder)
    {
        _fontEmbedder = fontEmbedder;
    }

    public async Task<string> RenderSvgAsync(
        byte[] bytes,
        string path,
        AltiumPreviewSide side,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await using var input = new MemoryStream(bytes, writable: false);
        await using var output = new MemoryStream();

        var renderer = new SvgRenderer();
        var options = new RenderOptions
        {
            Width = DefaultWidth,
            Height = DefaultHeight,
            AutoZoom = true,
        };

        switch (AltiumPreviewFileClassifier.GetKind(path))
        {
            case AltiumPreviewKind.SchematicDocument:
                var schematic = new SchDocReader().Read(input);
                await renderer.RenderAsync(schematic, output, options, cancellationToken);
                break;

            case AltiumPreviewKind.PcbDocument:
                var board = new PcbDocReader().Read(input);
                var style = PcbRealisticStyle.GreenEnig.For(ToPcbViewSide(side));
                await renderer.RenderRealisticAsync(board, output, options, style, cancellationToken);
                break;

            default:
                throw new InvalidOperationException("This file type is not supported by the Altium preview renderer.");
        }

        var svg = Encoding.UTF8.GetString(output.ToArray());
        return _fontEmbedder.EmbedUsedFonts(svg);
    }

    private static PcbViewSide ToPcbViewSide(AltiumPreviewSide side) =>
        side == AltiumPreviewSide.Bottom ? PcbViewSide.Bottom : PcbViewSide.Top;
}

public enum AltiumPreviewSide
{
    Top,
    Bottom,
}
