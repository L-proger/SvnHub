using System.Text;
using Microsoft.AspNetCore.StaticFiles;

namespace SvnHub.Web.Support;

public static class RepositoryFileClassifier
{
    public const int SniffByteCount = 8192;

    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

    public static bool IsPdfPath(string path) =>
        string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool LooksPdfContent(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 5 &&
        bytes[0] == (byte)'%' &&
        bytes[1] == (byte)'P' &&
        bytes[2] == (byte)'D' &&
        bytes[3] == (byte)'F' &&
        bytes[4] == (byte)'-';

    public static string GetContentTypeOrDefault(string fileName)
    {
        if (ContentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }

    public static string NormalizeRawTextContentType(string fileName, string contentType, ReadOnlySpan<byte> bytes = default)
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

        if (!bytes.IsEmpty && LooksTextContent(bytes))
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
            ".pro" or ".pri" or ".qrc" or
            ".cs" or ".csx" or ".csproj" or ".sln" or ".props" or ".targets" or
            ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or
            ".c" or ".h" or ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" or ".hxx" or
            ".v" or ".vh" or ".sv" or ".svh" or
            ".ps1" or ".psm1" or ".sh" or ".bat" or ".cmd";
    }

    public static bool LooksTextContent(ReadOnlySpan<byte> bytes) => !LooksBinary(bytes);

    public static bool LooksBinary(ReadOnlySpan<byte> bytes)
    {
        var len = Math.Min(bytes.Length, SniffByteCount);
        if (len == 0)
        {
            return false;
        }

        var suspiciousControlBytes = 0;
        for (var i = 0; i < len; i++)
        {
            var b = bytes[i];
            if (b == 0)
            {
                return true;
            }

            if ((b < 0x20 && b is not (0x08 or 0x09 or 0x0A or 0x0C or 0x0D or 0x1B)) || b == 0x7F)
            {
                suspiciousControlBytes++;
            }
        }

        return suspiciousControlBytes > Math.Max(8, len / 20);
    }

    public static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            }
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    public static string DecodeUtf8(byte[] bytes) => DecodeText(bytes);

    private static bool IsMarkdownFileName(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownExtensionlessTextFile(string fileName) =>
        string.Equals(fileName, "README", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "LICENSE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "NOTICE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fileName, "CHANGELOG", StringComparison.OrdinalIgnoreCase);
}
