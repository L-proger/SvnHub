using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace SvnHub.Web.Support;

public static class RepositoryFileClassifier
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static string GuessLanguage(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return "plaintext";
        }

        ext = ext.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "md" => "markdown",
            "cs" => "csharp",
            "c" => "c",
            "h" => "c",
            "cc" => "cpp",
            "cpp" => "cpp",
            "cxx" => "cpp",
            "hpp" => "cpp",
            "hh" => "cpp",
            "hxx" => "cpp",
            "v" => "verilog",
            "vh" => "verilog",
            "sv" => "verilog",
            "svh" => "verilog",
            _ => "plaintext",
        };
    }

    public static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    public static string GetContentTypeOrDefault(string fileName)
    {
        if (ContentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }

    public static string NormalizeRawTextContentType(string fileName, string contentType)
    {
        if (IsMarkdownFileName(fileName))
        {
            return "text/plain; charset=utf-8";
        }

        if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase)
                ? contentType
                : contentType + "; charset=utf-8";
        }

        if (LooksTextByFileName(fileName))
        {
            return "text/plain; charset=utf-8";
        }

        return contentType;
    }

    public static bool LooksTextByFileName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (IsKnownExtensionlessTextFile(fileName))
        {
            return true;
        }

        if (ContentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            if (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "application/javascript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "application/x-javascript", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "application/x-sh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is
            ".md" or ".markdown" or ".mkd" or ".rst" or ".adoc" or ".asciidoc" or
            ".txt" or ".log" or ".ini" or ".cfg" or ".conf" or ".config" or
            ".yml" or ".yaml" or
            ".cs" or ".csx" or ".csproj" or ".sln" or ".props" or ".targets" or
            ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or
            ".c" or ".h" or ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" or ".hxx" or
            ".v" or ".vh" or ".sv" or ".svh" or
            ".ps1" or ".psm1" or ".sh" or ".bat" or ".cmd";
    }

    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var len = Math.Min(bytes.Length, 8192);
        for (var i = 0; i < len; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }

    public static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool IsMarkdownFileName(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownExtensionlessTextFile(string fileName) =>
        string.Equals(fileName, "README", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "LICENSE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "NOTICE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "CHANGELOG", StringComparison.OrdinalIgnoreCase);
}
