using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class FileModel : PageModel
{
    private const int MaxChars = 1_000_000;
    private const int MaxDownloadBytes = 25_000_000;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;

    public FileModel(RepositoryService repos, AccessService access, ISvnLookClient svnlook)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
    }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long Revision { get; private set; }
    public string Contents { get; private set; } = "";
    public bool IsTruncated { get; private set; }
    public string? Error { get; private set; }
    public string HighlightedHtml { get; private set; } = "";
    public bool IsMarkdown { get; private set; }
    public string MarkdownHtml { get; private set; } = "";
    public bool IsImage { get; private set; }
    public string ImageContentType { get; private set; } = "application/octet-stream";

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);
        ParentPath = GetParent(Path);
        var language = GuessLanguage(Path);
        IsImage = IsImagePath(Path);
        ImageContentType = GetContentTypeOrDefault(Path);

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        try
        {
            Revision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            if (IsImage)
            {
                // Render via Raw handler (binary); don't try to read it as text.
                Contents = "";
                HighlightedHtml = "";
                MarkdownHtml = "";
                IsMarkdown = false;
                return Page();
            }

            var content = await _svnlook.CatAsync(repo.LocalPath, Path, Revision, cancellationToken);
            if (content.Length > MaxChars)
            {
                Contents = content[..MaxChars];
                IsTruncated = true;
            }
            else
            {
                Contents = content;
            }

            IsMarkdown = string.Equals(language, "markdown", StringComparison.Ordinal);
            if (IsMarkdown)
            {
                MarkdownHtml = MarkdownRenderer.Render(Contents);
                HighlightedHtml = "";
            }
            else
            {
                HighlightedHtml = SimpleSyntaxHighlighter.Highlight(Contents, language);
                MarkdownHtml = "";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        long rev;
        byte[] content;
        try
        {
            rev = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            content = await _svnlook.CatBytesAsync(repo.LocalPath, Path, rev, cancellationToken);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        if (content.Length > MaxDownloadBytes)
        {
            content = content[..MaxDownloadBytes];
        }

        var fileName = System.IO.Path.GetFileName(Path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        var contentType = GetContentTypeOrDefault(fileName);

        Response.Headers.ETag = $"W/\"{repoName}:{rev}:{Path}\"";
        return File(content, contentType, fileName);
    }

    public async Task<IActionResult> OnGetRawAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);

        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Read)
        {
            return Forbid();
        }

        long rev;
        byte[] content;
        try
        {
            rev = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            content = await _svnlook.CatBytesAsync(repo.LocalPath, Path, rev, cancellationToken);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        if (content.Length > MaxDownloadBytes)
        {
            content = content[..MaxDownloadBytes];
        }

        var fileName = System.IO.Path.GetFileName(Path);
        var contentType = GetContentTypeOrDefault(fileName);

        Response.Headers.ETag = $"W/\"{repoName}:{rev}:{Path}\"";
        return File(content, contentType);
    }

    private static string GuessLanguage(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
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

    private static bool IsImagePath(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
    }

    private static string GetContentTypeOrDefault(string fileName)
    {
        if (ContentTypeProvider.TryGetContentType(fileName, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var p = path.Trim();
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

    private static string GetParent(string path)
    {
        if (path == "/")
        {
            return "/";
        }

        var idx = path.LastIndexOf('/');
        if (idx <= 0)
        {
            return "/";
        }

        return path[..idx];
    }
}
