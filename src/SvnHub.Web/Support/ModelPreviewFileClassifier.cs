namespace SvnHub.Web.Support;

public static class ModelPreviewFileClassifier
{
    private static readonly HashSet<string> StandaloneExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3dm", ".3ds", ".3mf", ".amf", ".bim", ".brep", ".brp", ".dae", ".fbx", ".fcstd",
        ".ifc", ".iges", ".igs", ".off", ".ply", ".step", ".stp",
        ".stl", ".wrl",
    };

    private static readonly HashSet<string> SidecarCapableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".glb", ".gltf", ".obj",
    };

    private static readonly HashSet<string> SidecarAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".bmp", ".gif", ".jpeg", ".jpg", ".mtl", ".png", ".tga", ".tif", ".tiff",
        ".webp", ".zip",
    };

    public static bool IsImportableModelPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) &&
            (StandaloneExtensions.Contains(ext) || SidecarCapableExtensions.Contains(ext));
    }

    public static bool IsStandaloneModelPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) && StandaloneExtensions.Contains(ext);
    }

    public static bool IsSidecarCapableModelPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) && SidecarCapableExtensions.Contains(ext);
    }

    public static bool IsSidecarAssetPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) && SidecarAssetExtensions.Contains(ext);
    }

    public static bool ShouldIncludeInPreviewSet(string currentPath, string candidatePath)
    {
        if (string.Equals(currentPath, candidatePath, StringComparison.Ordinal))
        {
            return true;
        }

        return IsSidecarCapableModelPath(currentPath) && IsSidecarAssetPath(candidatePath);
    }

    public static string Describe(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
        return ext.Length == 0 ? "3D model asset" : ext switch
        {
            "STP" => "STEP",
            "IGS" => "IGES",
            "BRP" => "BREP",
            "GLB" => "glTF binary",
            "GLTF" => "glTF",
            "MTL" => "OBJ material",
            "BIN" => "Binary buffer",
            "PNG" or "JPG" or "JPEG" or "WEBP" or "BMP" or "TGA" or "TIF" or "TIFF" or "GIF" => "Texture",
            _ => ext,
        };
    }
}

public sealed record ModelPreviewFile(string Name, string Path, string Kind, long SizeBytes, string SizeLabel);
