using System.Text;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;

namespace SvnHub.Web.Support;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = CreatePipeline();

    public static string Render(string markdown) => Render(markdown, context: null);

    public static string Render(string markdown, string repoName, string currentPath) =>
        Render(markdown, new MarkdownContext(repoName, currentPath, Revision: null));

    public static string Render(string markdown, string repoName, string currentPath, long? revision) =>
        Render(markdown, new MarkdownContext(repoName, currentPath, revision));

    private static string Render(string markdown, MarkdownContext? context)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return "";
        }

        // GitHub renders Markdown inside <details> blocks (a non-standard behavior).
        // We emulate this by expanding <details> blocks into HTML with rendered inner Markdown.
        var expanded = context is null
            ? markdown
            : ExpandDetailsBlocks(markdown, context, depth: 0);

        var html = Markdown.ToHtml(expanded, Pipeline);
        return context is null ? html : SanitizeAndRewrite(html, context);
    }

    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks();

        return builder.Build();
    }

    private static string ExpandDetailsBlocks(string markdown, MarkdownContext context, int depth)
    {
        // Avoid pathological recursion.
        if (depth >= 4)
        {
            return markdown;
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder(markdown.Length + 256);

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith("<details", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(line);
                i++;
                continue;
            }

            var openLine = trimmed;
            var hasOpen =
                openLine.Contains(" open", StringComparison.OrdinalIgnoreCase) ||
                openLine.Contains("\topen", StringComparison.OrdinalIgnoreCase) ||
                openLine.Contains("open>", StringComparison.OrdinalIgnoreCase) ||
                openLine.Contains("open=\"", StringComparison.OrdinalIgnoreCase) ||
                openLine.Contains("open='", StringComparison.OrdinalIgnoreCase);

            i++;

            string? summaryText = null;
            var inner = new List<string>();

            while (i < lines.Length)
            {
                var innerLine = lines[i];
                var innerTrim = innerLine.Trim();
                i++;

                if (innerTrim.StartsWith("</details", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (summaryText is null &&
                    innerTrim.StartsWith("<summary", StringComparison.OrdinalIgnoreCase) &&
                    innerTrim.Contains("</summary>", StringComparison.OrdinalIgnoreCase))
                {
                    summaryText = ExtractSummaryText(innerTrim);
                    continue;
                }

                inner.Add(innerLine);
            }

            sb.Append("<details class=\"md-details\"");
            if (hasOpen)
            {
                sb.Append(" open");
            }
            sb.AppendLine(">");

            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                var summaryHtml = Markdown.ToHtml(summaryText, Pipeline).Trim();
                summaryHtml = StripOuterParagraph(summaryHtml);
                sb.Append("<summary>");
                sb.Append(summaryHtml);
                sb.AppendLine("</summary>");
            }

            if (inner.Count != 0)
            {
                var innerMd = string.Join("\n", inner);
                var innerExpanded = ExpandDetailsBlocks(innerMd, context, depth + 1);
                var innerHtml = Markdown.ToHtml(innerExpanded, Pipeline);
                sb.AppendLine(innerHtml);
            }

            sb.AppendLine("</details>");
        }

        return sb.ToString();
    }

    private static string? ExtractSummaryText(string summaryLine)
    {
        var gt = summaryLine.IndexOf('>');
        var close = summaryLine.LastIndexOf("</summary>", StringComparison.OrdinalIgnoreCase);
        if (gt < 0 || close <= gt)
        {
            return null;
        }

        return summaryLine[(gt + 1)..close];
    }

    private static string StripOuterParagraph(string html)
    {
        var trimmed = html.Trim();
        if (trimmed.StartsWith("<p>", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("</p>", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[3..^4].Trim();
        }

        return trimmed;
    }

    private static string SanitizeAndRewrite(string html, MarkdownContext context)
    {
        var sanitizer = CreateSanitizer();

        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not IElement el)
            {
                return;
            }

            switch (el.TagName.ToUpperInvariant())
            {
                case "A":
                    RewriteAnchor(el, context);
                    break;
                case "IMG":
                    RewriteImage(el, context);
                    break;
                case "TABLE":
                    DecorateTable(el);
                    break;
                case "DETAILS":
                    el.ClassList.Add("md-details");
                    break;
                case "INPUT":
                    // Markdig task lists render checkboxes. Ensure they're inert.
                    if (string.Equals(el.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase))
                    {
                        el.SetAttribute("disabled", "");
                    }
                    break;
            }
        };

        return sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");
        sanitizer.AllowedSchemes.Add("mailto");

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "div",
                     "p", "br", "hr",
                     "h1", "h2", "h3", "h4", "h5", "h6",
                     "strong", "em", "del",
                     "code", "pre", "span",
                     "blockquote",
                     "ul", "ol", "li",
                     "a", "img",
                     "table", "thead", "tbody", "tr", "th", "td",
                     "details", "summary",
                     "input",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[]
                 {
                     "href", "src", "alt", "title",
                     "class",
                     "id",
                     "name",
                     "width", "height",
                     "align",
                     "open",
                     "type", "checked", "disabled",
                 })
        {
            sanitizer.AllowedAttributes.Add(attr);
        }

        // Disallow inline styles; align is supported via attribute -> CSS class mapping.
        sanitizer.AllowedCssProperties.Clear();

        return sanitizer;
    }

    private static void RewriteAnchor(IElement el, MarkdownContext context)
    {
        var href = el.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        if (!IsAllowedUrl(href))
        {
            el.RemoveAttribute("href");
            return;
        }

        var resolved = ResolveLinkUrl(href, context);
        if (!string.Equals(resolved, href, StringComparison.Ordinal))
        {
            el.SetAttribute("href", resolved);
        }
    }

    private static void RewriteImage(IElement el, MarkdownContext context)
    {
        var src = el.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            return;
        }

        if (!IsAllowedUrl(src))
        {
            el.RemoveAttribute("src");
            return;
        }

        var resolved = ResolveImageUrl(src, context);
        el.SetAttribute("src", resolved);

        el.SetAttribute("loading", "lazy");
        el.ClassList.Add("md-img");

        var align = el.GetAttribute("align");
        if (!string.IsNullOrWhiteSpace(align))
        {
            var a = align.Trim().ToLowerInvariant();
            switch (a)
            {
                case "left":
                    el.ClassList.Add("md-img-align-left");
                    break;
                case "right":
                    el.ClassList.Add("md-img-align-right");
                    break;
                case "center":
                    el.ClassList.Add("md-img-align-center");
                    break;
            }
        }
    }

    private static void DecorateTable(IElement table)
    {
        table.ClassList.Add("md-table");

        // Wrap with a scroll container if not already wrapped.
        if (table.ParentElement is { } parent && parent.ClassList.Contains("md-table-wrap"))
        {
            return;
        }

        var doc = table.Owner;
        if (doc is null)
        {
            return;
        }

        var wrap = doc.CreateElement("div");
        wrap.ClassList.Add("md-table-wrap");

        var originalParent = table.Parent;
        if (originalParent is null)
        {
            return;
        }

        originalParent.ReplaceChild(wrap, table);
        wrap.AppendChild(table);
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

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto";
        }

        // relative URLs are allowed (e.g. "docs/page.md")
        return !url.Contains(':');
    }

    private static string ResolveLinkUrl(string url, MarkdownContext context)
    {
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
            ? BuildTreeUrl(context.RepoName, svnPath, context.Revision)
            : BuildFileUrl(context.RepoName, svnPath, context.Revision);

        return baseUrl + fragment;
    }

    private static string ResolveImageUrl(string url, MarkdownContext context)
    {
        var raw = url.Trim();
        if (raw.Length == 0 || raw.StartsWith('#'))
        {
            return raw;
        }

        if (Uri.TryCreate(raw, UriKind.Absolute, out _))
        {
            return raw;
        }

        // For repository images, ignore any query/fragment to avoid passing arbitrary params to handlers.
        var cut = raw.IndexOfAny(['?', '#']);
        var rawPath = cut >= 0 ? raw[..cut] : raw;

        // Normalize separators for Windows-authored docs.
        rawPath = rawPath.Replace('\\', '/');

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

        return BuildRawFileUrl(context.RepoName, svnPath, context.Revision);
    }

    private static string BuildTreeUrl(string repoName, string svnPath, long? revision)
    {
        var repoSegment = Uri.EscapeDataString(repoName);
        if (string.IsNullOrWhiteSpace(svnPath) || svnPath == "/")
        {
            return revision is null
                ? $"/repos/{repoSegment}/tree"
                : $"/repos/{repoSegment}/tree?rev={revision.Value}";
        }

        return revision is null
            ? $"/repos/{repoSegment}/tree?path={Uri.EscapeDataString(svnPath)}"
            : $"/repos/{repoSegment}/tree?path={Uri.EscapeDataString(svnPath)}&rev={revision.Value}";
    }

    private static string BuildFileUrl(string repoName, string svnPath, long? revision)
    {
        var repoSegment = Uri.EscapeDataString(repoName);
        return revision is null
            ? $"/repos/{repoSegment}/file?path={Uri.EscapeDataString(svnPath)}"
            : $"/repos/{repoSegment}/file?path={Uri.EscapeDataString(svnPath)}&rev={revision.Value}";
    }

    private static string BuildRawFileUrl(string repoName, string svnPath, long? revision)
    {
        var repoSegment = Uri.EscapeDataString(repoName);
        return revision is null
            ? $"/repos/{repoSegment}/file?handler=Raw&path={Uri.EscapeDataString(svnPath)}"
            : $"/repos/{repoSegment}/file?handler=Raw&path={Uri.EscapeDataString(svnPath)}&rev={revision.Value}";
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

    private sealed record MarkdownContext(string RepoName, string CurrentPath, long? Revision);
}
