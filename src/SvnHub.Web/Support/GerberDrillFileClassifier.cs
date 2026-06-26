using System.Text.RegularExpressions;

namespace SvnHub.Web.Support;

public static partial class GerberDrillFileClassifier
{
    private static readonly HashSet<string> GerberExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".art", ".bot", ".cmp", ".dim", ".fab", ".gbp", ".gbl", ".gbo", ".gbr", ".gbs", ".gbx",
        ".ger", ".gko", ".gml", ".gpi", ".gtl", ".gto", ".gtp", ".gts", ".ly1", ".ly2",
        ".mil", ".pho", ".plc", ".pls", ".sol", ".stc", ".sts", ".top",
    };

    private static readonly HashSet<string> DrillExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cnc", ".drl", ".drd", ".exc", ".ncd", ".nc", ".ncp", ".npt", ".tap", ".xln",
    };

    public static bool IsBoardFileCandidate(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var ext = Path.GetExtension(fileName);
        if (GerberExtensions.Contains(ext) || DrillExtensions.Contains(ext))
        {
            return true;
        }

        if (GeneratedGerberExtensionRegex().IsMatch(ext) || GeneratedOutlineExtensionRegex().IsMatch(ext))
        {
            return true;
        }

        return string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase) && LooksLikeDrillText(fileName);
    }

    public static string Describe(string path)
    {
        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        return ext switch
        {
            ".gtl" or ".top" => "Top copper",
            ".gbl" or ".bot" => "Bottom copper",
            ".gts" => "Top solder mask",
            ".gbs" => "Bottom solder mask",
            ".gto" => "Top silkscreen",
            ".gbo" => "Bottom silkscreen",
            ".gtp" => "Top paste",
            ".gbp" => "Bottom paste",
            ".gko" or ".gml" or ".gm" or ".dim" => "Board outline",
            ".drl" or ".xln" or ".exc" or ".tap" or ".nc" or ".cnc" => "Drill",
            ".txt" when LooksLikeDrillText(fileName) => "Drill",
            _ when GeneratedOutlineExtensionRegex().IsMatch(ext) => "Board outline",
            _ when GeneratedGerberExtensionRegex().IsMatch(ext) => "Gerber layer",
            _ => "CAM layer",
        };
    }

    private static bool LooksLikeDrillText(string fileName) =>
        fileName.Contains("drill", StringComparison.OrdinalIgnoreCase) ||
        fileName.Contains("plated", StringComparison.OrdinalIgnoreCase) ||
        fileName.Contains("pth", StringComparison.OrdinalIgnoreCase) ||
        fileName.Contains("npth", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\.g(?:p)?\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedGerberExtensionRegex();

    [GeneratedRegex(@"^\.gm\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedOutlineExtensionRegex();
}

public sealed record GerberPreviewFile(string Name, string Path, string Kind, long SizeBytes, string SizeLabel);
