using System.Text;
using System.Xml.Linq;

namespace SvnHub.Web.Support;

public sealed class AltiumSvgFontEmbedder
{
    private const string TypeAFontFamily = "GOST type A";
    private const string TypeAuFontFamily = "GOST type AU";
    private const string TypeBFontFamily = "GOST type B";
    private const string TypeBuFontFamily = "GOST type BU";
    private readonly string _typeADataUrl;
    private readonly string _typeBDataUrl;

    public AltiumSvgFontEmbedder(IWebHostEnvironment environment)
    {
        var webRootPath = environment.WebRootPath
            ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var fontDirectory = Path.Combine(webRootPath, "fonts", "opengost");

        _typeADataUrl = ReadFontDataUrl(Path.Combine(fontDirectory, "OpenGostTypeA-Regular.ttf"));
        _typeBDataUrl = ReadFontDataUrl(Path.Combine(fontDirectory, "OpenGostTypeB-Regular.ttf"));
    }

    public string EmbedUsedFonts(string svg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svg);

        if (svg[0] == '\uFEFF')
        {
            svg = svg[1..];
        }

        var document = XDocument.Parse(svg, LoadOptions.PreserveWhitespace);
        var root = document.Root
            ?? throw new InvalidOperationException("The generated SVG has no root element.");
        var fontFamilies = root
            .DescendantsAndSelf()
            .Attributes("font-family")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var css = new StringBuilder();
        if (fontFamilies.Contains(TypeAFontFamily))
        {
            AppendFontFace(css, TypeAFontFamily, _typeADataUrl);
        }

        if (fontFamilies.Contains(TypeAuFontFamily))
        {
            AppendFontFace(css, TypeAuFontFamily, _typeADataUrl);
        }

        if (fontFamilies.Contains(TypeBFontFamily))
        {
            AppendFontFace(css, TypeBFontFamily, _typeBDataUrl);
        }

        if (fontFamilies.Contains(TypeBuFontFamily))
        {
            AppendFontFace(css, TypeBuFontFamily, _typeBDataUrl);
        }

        if (css.Length == 0)
        {
            return svg;
        }

        var svgNamespace = root.Name.Namespace;
        var definitions = root.Elements()
            .FirstOrDefault(element => element.Name.LocalName == "defs");
        if (definitions is null)
        {
            definitions = new XElement(svgNamespace + "defs");
            root.AddFirst(definitions);
        }

        definitions.AddFirst(
            new XElement(
                svgNamespace + "style",
                new XAttribute("type", "text/css"),
                new XCData(css.ToString())));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static string ReadFontDataUrl(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return $"data:font/ttf;base64,{Convert.ToBase64String(bytes)}";
    }

    private static void AppendFontFace(StringBuilder css, string fontFamily, string dataUrl)
    {
        css.Append("@font-face{font-family:\"")
            .Append(fontFamily)
            .Append("\";src:url(\"")
            .Append(dataUrl)
            .Append("\") format(\"truetype\");font-style:normal;font-weight:400;}\n");
    }
}
