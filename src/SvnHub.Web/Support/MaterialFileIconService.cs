using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvnHub.Web.Support;

public sealed class MaterialFileIconService
{
    private const string AssetRootHref = "~/lib/material-icons/icons/";

    private readonly IWebHostEnvironment _environment;
    private readonly Lazy<MaterialIconTheme?> _theme;

    public MaterialFileIconService(IWebHostEnvironment environment)
    {
        _environment = environment;
        _theme = new Lazy<MaterialIconTheme?>(LoadTheme);
    }

    public string? GetIconHref(string path, bool isDirectory, bool isOpen = false)
    {
        var theme = _theme.Value;
        if (theme is null)
        {
            return null;
        }

        var iconName = isDirectory
            ? FindFolderIconName(theme, path, isOpen)
            : FindFileIconName(theme, path);

        return ResolveIconHref(theme, iconName);
    }

    public string? GetRepositoryIconHref(bool isOpen = false)
    {
        var theme = _theme.Value;
        if (theme is null)
        {
            return null;
        }

        return ResolveIconHref(theme, isOpen ? "folder-repository-open" : "folder-repository");
    }

    private static string FindFolderIconName(MaterialIconTheme theme, string path, bool isOpen)
    {
        var folderName = GetBaseName(path).ToLowerInvariant();
        var folderMap = isOpen ? theme.FolderNamesExpanded : theme.FolderNames;
        if (!string.IsNullOrWhiteSpace(folderName) &&
            folderMap.TryGetValue(folderName, out var iconName))
        {
            return iconName;
        }

        return isOpen ? theme.FolderExpanded : theme.Folder;
    }

    private static string FindFileIconName(MaterialIconTheme theme, string path)
    {
        var normalizedPath = NormalizeKey(path);
        var fileName = GetBaseName(normalizedPath).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalizedPath) &&
            theme.FileNames.TryGetValue(normalizedPath, out var iconName))
        {
            return iconName;
        }

        if (!string.IsNullOrWhiteSpace(fileName) &&
            theme.FileNames.TryGetValue(fileName, out iconName))
        {
            return iconName;
        }

        for (var i = fileName.LastIndexOf('.'); i >= 0; i = i <= 0 ? -1 : fileName.LastIndexOf('.', i - 1))
        {
            var extension = fileName[(i + 1)..];
            if (extension.Length > 0 && theme.FileExtensions.TryGetValue(extension, out iconName))
            {
                return iconName;
            }
        }

        return theme.File;
    }

    private string? ResolveIconHref(MaterialIconTheme theme, string? iconName)
    {
        if (string.IsNullOrWhiteSpace(iconName) ||
            !theme.IconDefinitions.TryGetValue(iconName, out var definition))
        {
            return null;
        }

        var iconPath = definition.IconPath.Replace('\\', '/');
        var iconFileName = Path.GetFileName(iconPath);
        if (string.IsNullOrWhiteSpace(iconFileName) ||
            !iconFileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var iconFile = _environment.WebRootFileProvider.GetFileInfo($"lib/material-icons/icons/{iconFileName}");
        return iconFile.Exists ? AssetRootHref + iconFileName : null;
    }

    private MaterialIconTheme? LoadTheme()
    {
        var jsonFile = _environment.WebRootFileProvider.GetFileInfo("lib/material-icons/material-icons.json");
        if (!jsonFile.Exists)
        {
            return null;
        }

        try
        {
            using var stream = jsonFile.CreateReadStream();
            var theme = JsonSerializer.Deserialize<MaterialIconTheme>(
                stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (theme is null)
            {
                return null;
            }

            theme.NormalizeLookups();
            return theme;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeKey(string path)
    {
        var normalized = (path ?? "").Trim().Replace('\\', '/').Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.ToLowerInvariant();
    }

    private static string GetBaseName(string path)
    {
        var normalized = (path ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        if (normalized.Length == 0)
        {
            return "";
        }

        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private sealed class MaterialIconTheme
    {
        public Dictionary<string, MaterialIconDefinition> IconDefinitions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FileNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FileExtensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FolderNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> FolderNamesExpanded { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string File { get; set; } = "file";
        public string Folder { get; set; } = "folder";
        public string FolderExpanded { get; set; } = "folder-open";

        public void NormalizeLookups()
        {
            IconDefinitions = NormalizeDictionary(IconDefinitions);
            FileNames = NormalizeDictionary(FileNames);
            FileExtensions = NormalizeDictionary(FileExtensions);
            FolderNames = NormalizeDictionary(FolderNames);
            FolderNamesExpanded = NormalizeDictionary(FolderNamesExpanded);
        }

        private static Dictionary<string, TValue> NormalizeDictionary<TValue>(IDictionary<string, TValue>? values)
        {
            var result = new Dictionary<string, TValue>(StringComparer.OrdinalIgnoreCase);
            if (values is null)
            {
                return result;
            }

            foreach (var (key, value) in values)
            {
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }
    }

    private sealed class MaterialIconDefinition
    {
        [JsonPropertyName("iconPath")]
        public string IconPath { get; init; } = "";
    }
}
