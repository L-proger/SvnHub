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
        var stem = Path.GetFileNameWithoutExtension(fileName);

        if (TryDescribeShortGerberName(ext, stem, out var shortNameDescription))
        {
            return shortNameDescription;
        }

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

    public static string GetStackupFileName(string path)
    {
        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);

        if (TryGetStackupExtension(ext, stem, out var stackupExtension))
        {
            return $"board{stackupExtension}";
        }

        return fileName;
    }

    private static bool TryDescribeShortGerberName(string extension, string stem, out string description)
    {
        if (!string.Equals(extension, ".gbr", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".ger", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".pho", StringComparison.OrdinalIgnoreCase))
        {
            description = "";
            return false;
        }

        if (TryGetStackupExtension(extension, stem, out var stackupExtension))
        {
            description = stackupExtension.ToLowerInvariant() switch
            {
                ".gtl" => "Top copper",
                ".gbl" => "Bottom copper",
                ".gts" => "Top solder mask",
                ".gbs" => "Bottom solder mask",
                ".gto" => "Top silkscreen",
                ".gbo" => "Bottom silkscreen",
                ".gtp" => "Top paste",
                ".gbp" => "Bottom paste",
                ".gko" => "Board outline",
                ".drl" => "Drill",
                _ when GeneratedGerberExtensionRegex().IsMatch(stackupExtension) => "Inner copper",
                _ => "CAM layer",
            };
            return true;
        }

        description = "";
        return false;
    }

    private static bool TryGetStackupExtension(string extension, string stem, out string stackupExtension)
    {
        if (!string.Equals(extension, ".gbr", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".ger", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".pho", StringComparison.OrdinalIgnoreCase))
        {
            stackupExtension = "";
            return false;
        }

        var key = stem.Trim().ToLowerInvariant();
        stackupExtension = key switch
        {
            "tl" or "top" => ".GTL",
            "bl" or "bot" or "bottom" => ".GBL",
            "ts" or "smt" or "topsolder" or "topsoldermask" => ".GTS",
            "bs" or "smb" or "bottomsolder" or "bottomsoldermask" => ".GBS",
            "to" or "sst" or "topoverlay" or "topsilk" or "topsilkscreen" => ".GTO",
            "bo" or "ssb" or "bottomoverlay" or "bottomsilk" or "bottomsilkscreen" => ".GBO",
            "tp" or "spt" or "toppaste" => ".GTP",
            "bp" or "spb" or "bottompaste" => ".GBP",
            "drl" or "drill" or "drills" => ".DRL",
            "rout" or "route" or "outline" or "profile" => ".GKO",
            _ => "",
        };

        if (!string.IsNullOrEmpty(stackupExtension))
        {
            return true;
        }

        var innerMatch = InnerLayerStemRegex().Match(key);
        if (innerMatch.Success)
        {
            stackupExtension = $".G{innerMatch.Groups[1].Value}";
            return true;
        }

        return false;
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

    [GeneratedRegex(@"^(?:l|ly|layer)(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InnerLayerStemRegex();
}

public sealed record GerberPreviewFile(
    string Name,
    string Path,
    string Kind,
    long SizeBytes,
    string SizeLabel,
    string StackupFileName);
