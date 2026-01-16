using System.Text;

namespace SvnHub.Web.Support;

public static class MarkdownRenderer
{
    public static string Render(string markdown) => Render(markdown, context: null);

    public static string Render(string markdown, string repoName, string currentPath) =>
        Render(markdown, new MarkdownContext(repoName, currentPath));

    private static string Render(string markdown, MarkdownContext? context)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return "";
        }

        var sb = new StringBuilder(markdown.Length + Math.Min(32_000, markdown.Length / 2));

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var i = 0;
        var inCodeFence = false;
        var codeFenceLang = "";
        var codeFence = new StringBuilder();

        var listMode = ListMode.None;
        var paragraph = new List<string>();
        var blockquote = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            sb.Append("<p>");
            sb.Append(RenderInline(string.Join(" ", paragraph), context));
            sb.AppendLine("</p>");
            paragraph.Clear();
        }

        void FlushBlockquote()
        {
            if (blockquote.Count == 0) return;
            sb.Append("<blockquote>");
            sb.Append(RenderBlock(string.Join("\n", blockquote), context));
            sb.AppendLine("</blockquote>");
            blockquote.Clear();
        }

        void OpenList(ListMode mode)
        {
            if (listMode != ListMode.None) return;
            listMode = mode;
            sb.AppendLine(mode == ListMode.Unordered ? "<ul>" : "<ol>");
        }

        void CloseList()
        {
            if (listMode == ListMode.None) return;
            sb.AppendLine(listMode == ListMode.Unordered ? "</ul>" : "</ol>");
            listMode = ListMode.None;
        }

        void FlushAllBlocks()
        {
            FlushBlockquote();
            CloseList();
            FlushParagraph();
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            i++;

            if (inCodeFence)
            {
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    var language = NormalizeCodeFenceLanguage(codeFenceLang);
                    var highlighted = SimpleSyntaxHighlighter.Highlight(codeFence.ToString(), language);
                    sb.Append("<pre><code class=\"sh\">");
                    sb.Append(highlighted);
                    sb.AppendLine("</code></pre>");

                    inCodeFence = false;
                    codeFenceLang = "";
                    codeFence.Clear();
                    continue;
                }

                codeFence.AppendLine(line);
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushAllBlocks();
                inCodeFence = true;
                codeFenceLang = line.Trim('`', ' ').Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushAllBlocks();
                continue;
            }

            // Headings: # ... ######
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
                {
                    level++;
                }

                if (level > 0 && (level == trimmed.Length || trimmed[level] == ' '))
                {
                    FlushAllBlocks();
                    var content = trimmed[level..].Trim();
                    sb.Append("<h");
                    sb.Append(level);
                    sb.Append(">");
                    sb.Append(RenderInline(content, context));
                    sb.Append("</h");
                    sb.Append(level);
                    sb.AppendLine(">");
                    continue;
                }
            }

            // Blockquote: > ...
            if (trimmed.StartsWith("> ", StringComparison.Ordinal) || string.Equals(trimmed, ">", StringComparison.Ordinal))
            {
                CloseList();
                FlushParagraph();
                blockquote.Add(trimmed.Length >= 2 ? trimmed[2..] : "");
                continue;
            }

            // Unordered list: - / *
            if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushBlockquote();
                FlushParagraph();
                OpenList(ListMode.Unordered);
                sb.Append("<li>");
                sb.Append(RenderInline(trimmed[2..], context));
                sb.AppendLine("</li>");
                continue;
            }

            // Ordered list: "1. "
            if (TryParseOrderedListItem(trimmed, out var orderedText))
            {
                FlushBlockquote();
                FlushParagraph();
                OpenList(ListMode.Ordered);
                sb.Append("<li>");
                sb.Append(RenderInline(orderedText, context));
                sb.AppendLine("</li>");
                continue;
            }

            // Otherwise: paragraph line
            FlushBlockquote();
            CloseList();
            paragraph.Add(trimmed);
        }

        FlushAllBlocks();

        if (inCodeFence)
        {
            var language = NormalizeCodeFenceLanguage(codeFenceLang);
            var highlighted = SimpleSyntaxHighlighter.Highlight(codeFence.ToString(), language);
            sb.Append("<pre><code class=\"sh\">");
            sb.Append(highlighted);
            sb.AppendLine("</code></pre>");
        }

        return sb.ToString();
    }

    private static string NormalizeCodeFenceLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "plaintext";
        }

        var first = raw.Trim();
        var space = first.IndexOfAny([' ', '\t']);
        if (space >= 0)
        {
            first = first[..space];
        }

        first = first.Trim().ToLowerInvariant();

        return first switch
        {
            "csharp" => "csharp",
            "cs" => "csharp",
            "c#" => "csharp",
            "cpp" => "cpp",
            "c++" => "cpp",
            "cc" => "cpp",
            "cxx" => "cpp",
            "h" => "c",
            "hpp" => "cpp",
            "hh" => "cpp",
            "hxx" => "cpp",
            "c" => "c",
            "verilog" => "verilog",
            "sv" => "verilog",
            "systemverilog" => "verilog",
            _ => "plaintext",
        };
    }

    private static bool TryParseOrderedListItem(string line, out string itemText)
    {
        itemText = "";
        var dot = line.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot + 1 >= line.Length)
        {
            return false;
        }

        // require "N. " where N is digits
        for (var i = 0; i < dot; i++)
        {
            if (!char.IsDigit(line[i]))
            {
                return false;
            }
        }

        if (line[dot + 1] != ' ')
        {
            return false;
        }

        itemText = line[(dot + 2)..];
        return true;
    }

    private static string RenderBlock(string text, MarkdownContext? context)
    {
        // MVP: render block by escaping and applying inline formatting per line.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(text.Length + 64);
        for (var i = 0; i < lines.Length; i++)
        {
            if (i != 0) sb.Append("<br/>");
            sb.Append(RenderInline(lines[i], context));
        }
        return sb.ToString();
    }

    private static string RenderInline(string text, MarkdownContext? context)
    {
        // MVP inline formatting:
        // - `code`
        // - **bold**
        // - *italic*
        // - [text](url) with basic URL allowlist
        var sb = new StringBuilder(text.Length + 32);

        var i = 0;
        while (i < text.Length)
        {
            // Inline code
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > i + 1)
                {
                    sb.Append("<code>");
                    AppendHtmlEncoded(sb, text.AsSpan(i + 1, end - i - 1));
                    sb.Append("</code>");
                    i = end + 1;
                    continue;
                }
            }

            // Link [text](url)
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i + 1 && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 2)
                    {
                        var linkText = text.AsSpan(i + 1, closeBracket - i - 1);
                        var url = text.AsSpan(closeBracket + 2, closeParen - (closeBracket + 2)).ToString().Trim();

                        if (IsAllowedUrl(url))
                        {
                            sb.Append("<a href=\"");
                            AppendHtmlEncoded(sb, ResolveUrl(url, context));
                            sb.Append("\">");
                            sb.Append(RenderInline(linkText.ToString(), context));
                            sb.Append("</a>");
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }
            }

            // Bold **text**
            if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > i + 2)
                {
                    sb.Append("<strong>");
                    sb.Append(RenderInline(text[(i + 2)..end], context));
                    sb.Append("</strong>");
                    i = end + 2;
                    continue;
                }
            }

            // Italic *text*
            if (text[i] == '*')
            {
                var end = text.IndexOf('*', i + 1);
                if (end > i + 1)
                {
                    sb.Append("<em>");
                    sb.Append(RenderInline(text[(i + 1)..end], context));
                    sb.Append("</em>");
                    i = end + 1;
                    continue;
                }
            }

            AppendHtmlEncoded(sb, text.AsSpan(i, 1));
            i++;
        }

        return sb.ToString();
    }

    private static bool IsAllowedUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (url.StartsWith('#'))
        {
            return true;
        }

        if (url.StartsWith("/", StringComparison.Ordinal))
        {
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto";
        }

        // relative URLs are allowed (e.g. "docs/page.md")
        return !url.Contains(':');
    }

    private static string ResolveUrl(string url, MarkdownContext? context)
    {
        if (context is null)
        {
            return url;
        }

        var raw = url.Trim();
        if (raw.Length == 0 || raw.StartsWith('#'))
        {
            return raw;
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out _))
        {
            return raw;
        }

        var fragmentIndex = raw.IndexOf('#', StringComparison.Ordinal);
        var fragment = fragmentIndex >= 0 ? raw[fragmentIndex..] : "";
        var rawPath = fragmentIndex >= 0 ? raw[..fragmentIndex] : raw;

        // Normalize separators for Windows-authored docs.
        rawPath = rawPath.Replace('\\', '/');

        var isTree = rawPath.EndsWith("/", StringComparison.Ordinal);

        string svnPath;
        if (rawPath.StartsWith("/", StringComparison.Ordinal))
        {
            svnPath = NormalizeRepoPath(rawPath);
        }
        else
        {
            var baseDir = GetDirectoryPath(context.CurrentPath);
            svnPath = ResolveRelativePath(baseDir, rawPath);
        }

        var baseUrl = isTree
            ? BuildTreeUrl(context.RepoName, svnPath)
            : BuildFileUrl(context.RepoName, svnPath);

        return baseUrl + fragment;
    }

    private static string BuildTreeUrl(string repoName, string svnPath)
    {
        var repoSegment = Uri.EscapeDataString(repoName);
        if (string.IsNullOrWhiteSpace(svnPath) || svnPath == "/")
        {
            return $"/repos/{repoSegment}/tree";
        }

        return $"/repos/{repoSegment}/tree?path={Uri.EscapeDataString(svnPath)}";
    }

    private static string BuildFileUrl(string repoName, string svnPath)
    {
        var repoSegment = Uri.EscapeDataString(repoName);
        return $"/repos/{repoSegment}/file?path={Uri.EscapeDataString(svnPath)}";
    }

    private static string GetDirectoryPath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath) || currentPath == "/")
        {
            return "/";
        }

        var p = NormalizeRepoPath(currentPath);
        var idx = p.LastIndexOf('/');
        if (idx <= 0)
        {
            return "/";
        }

        return p[..idx];
    }

    private static string ResolveRelativePath(string baseDir, string relative)
    {
        var rel = relative.Trim();
        while (rel.StartsWith("./", StringComparison.Ordinal))
        {
            rel = rel[2..];
        }

        var baseSegments = NormalizeRepoPath(baseDir).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var relSegments = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var stack = new List<string>(baseSegments.Length + relSegments.Length);
        stack.AddRange(baseSegments);

        foreach (var seg in relSegments)
        {
            if (seg == ".")
            {
                continue;
            }

            if (seg == "..")
            {
                if (stack.Count != 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                continue;
            }

            stack.Add(seg);
        }

        return "/" + string.Join('/', stack);
    }

    private static string NormalizeRepoPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var p = path.Trim().Replace('\\', '/');
        if (!p.StartsWith('/'))
        {
            p = "/" + p;
        }

        while (p.Contains("//", StringComparison.Ordinal))
        {
            p = p.Replace("//", "/", StringComparison.Ordinal);
        }

        if (p.Length > 1 && p.EndsWith('/'))
        {
            p = p.TrimEnd('/');
        }

        if (p.Contains("/../", StringComparison.Ordinal) || p.EndsWith("/..", StringComparison.Ordinal) || p == "/..")
        {
            return "/";
        }

        return p;
    }

    private static void AppendHtmlEncoded(StringBuilder sb, ReadOnlySpan<char> text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '&':
                    sb.Append("&amp;");
                    break;
                case '<':
                    sb.Append("&lt;");
                    break;
                case '>':
                    sb.Append("&gt;");
                    break;
                case '"':
                    sb.Append("&quot;");
                    break;
                case '\'':
                    sb.Append("&#39;");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
    }

    private enum ListMode
    {
        None = 0,
        Unordered = 1,
        Ordered = 2,
    }

    private sealed record MarkdownContext(string RepoName, string CurrentPath);
}
