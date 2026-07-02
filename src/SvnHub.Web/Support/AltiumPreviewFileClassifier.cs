namespace SvnHub.Web.Support;

public static class AltiumPreviewFileClassifier
{
    public static bool IsPreviewablePath(string path) =>
        GetKind(path) is not AltiumPreviewKind.Unknown;

    public static bool IsProjectPath(string path) =>
        string.Equals(Path.GetExtension(path), ".PrjPcb", StringComparison.OrdinalIgnoreCase);

    public static AltiumPreviewKind GetKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.Equals(ext, ".SchDoc", StringComparison.OrdinalIgnoreCase))
        {
            return AltiumPreviewKind.SchematicDocument;
        }

        if (string.Equals(ext, ".PcbDoc", StringComparison.OrdinalIgnoreCase))
        {
            return AltiumPreviewKind.PcbDocument;
        }

        return AltiumPreviewKind.Unknown;
    }

    public static string Describe(AltiumPreviewKind kind) =>
        kind switch
        {
            AltiumPreviewKind.SchematicDocument => "Altium schematic preview",
            AltiumPreviewKind.PcbDocument => "Altium PCB preview",
            _ => "Altium preview",
        };
}

public enum AltiumPreviewKind
{
    Unknown = 0,
    SchematicDocument,
    PcbDocument,
}
