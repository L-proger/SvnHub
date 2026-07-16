using System.Text;
using System.Xml.Linq;

namespace SvnHub.Web.Support;

public sealed class AltiumSvgFontEmbedder
{
    private const string TypeAFontFamily = "GOST type A";
    private const string TypeAuFontFamily = "GOST type AU";
    private const string TypeBFontFamily = "GOST type B";
    private const string TypeBuFontFamily = "GOST type BU";
    private readonly Lazy<string> _typeADataUrl;
    private readonly Lazy<string> _typeBDataUrl;

    public AltiumSvgFontEmbedder(IWebHostEnvironment environment)
    {
        _typeADataUrl = new Lazy<string>(
            () => ReadFontDataUrl(environment, "OpenGostTypeA-Regular.ttf"));
        _typeBDataUrl = new Lazy<string>(
            () => ReadFontDataUrl(environment, "OpenGostTypeB-Regular.ttf"));
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
            AppendFontFace(css, TypeAFontFamily, _typeADataUrl.Value);
        }

        if (fontFamilies.Contains(TypeAuFontFamily))
        {
            AppendFontFace(css, TypeAuFontFamily, _typeADataUrl.Value);
        }

        if (fontFamilies.Contains(TypeBFontFamily))
        {
            AppendFontFace(css, TypeBFontFamily, _typeBDataUrl.Value);
        }

        if (fontFamilies.Contains(TypeBuFontFamily))
        {
            AppendFontFace(css, TypeBuFontFamily, _typeBDataUrl.Value);
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

    private static string ReadFontDataUrl(IWebHostEnvironment environment, string fileName)
    {
        var relativePath = Path.Combine("fonts", "opengost", fileName).Replace('\\', '/');
        var fileInfo = environment.WebRootFileProvider.GetFileInfo(relativePath);
        if (fileInfo.Exists)
        {
            using var stream = fileInfo.CreateReadStream();
            return ToFontDataUrl(stream);
        }

        var candidateRoots = new[]
        {
            environment.WebRootPath,
            Path.Combine(environment.ContentRootPath, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "SvnHub.Web", "wwwroot"),
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        };

        foreach (var root in candidateRoots.Where(root => !string.IsNullOrWhiteSpace(root)))
        {
            var path = Path.Combine(root!, "fonts", "opengost", fileName);
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return ToFontDataUrl(stream);
            }
        }

        throw new FileNotFoundException(
            $"Altium preview font '{fileName}' was not found under wwwroot/fonts/opengost.");
    }

    private static string ToFontDataUrl(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
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
