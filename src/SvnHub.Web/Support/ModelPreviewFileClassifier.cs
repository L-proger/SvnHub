namespace SvnHub.Web.Support;

public static class ModelPreviewFileClassifier
{
    private static readonly HashSet<string> ImportableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3dm", ".3ds", ".3mf", ".amf", ".bim", ".brep", ".brp", ".dae", ".fbx", ".fcstd",
        ".glb", ".gltf", ".ifc", ".iges", ".igs", ".obj", ".off", ".ply", ".step", ".stp",
        ".stl", ".wrl",
    };

    private static readonly HashSet<string> RelatedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".bmp", ".gif", ".jpeg", ".jpg", ".mtl", ".png", ".tga", ".tif", ".tiff",
        ".webp", ".zip",
    };

    public static bool IsImportableModelPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) && ImportableExtensions.Contains(ext);
    }

    public static bool IsRelatedModelPath(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(ext) &&
            (ImportableExtensions.Contains(ext) || RelatedExtensions.Contains(ext));
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
