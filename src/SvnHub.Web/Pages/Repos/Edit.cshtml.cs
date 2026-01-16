using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class EditModel : PageModel
{
    private const int MaxEditBytes = 1_000_000;
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly ISvnRepositoryWriter _writer;

    public EditModel(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        ISvnRepositoryWriter writer)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _writer = writer;
    }

    [TempData]
    public string? FlashError { get; set; }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long Revision { get; private set; }
    public string? Error { get; private set; }
    public bool IsBinary { get; private set; }
    public bool CanEditContents { get; private set; }
    public bool IsDirectory { get; private set; }

    [BindProperty]
    public EditInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);
        if (Path == "/")
        {
            return BadRequest("Refusing to edit repository root.");
        }
        ParentPath = GetParent(Path);

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

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Write)
        {
            return Forbid();
        }

        try
        {
            Revision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            Input.OriginalPath = Path;
            Input.FileName = System.IO.Path.GetFileName(Path);
            Input.CommitMessage = "";

            // Try to treat it as a file first.
            if (LooksTextByFileName(Path))
            {
                try
                {
                    var bytes = await _svnlook.CatBytesAsync(repo.LocalPath, Path, Revision, cancellationToken);
                    if (bytes.Length > MaxEditBytes)
                    {
                        CanEditContents = false;
                        IsBinary = true;
                        IsDirectory = false;
                        Input.IsDirectory = false;
                        Input.Contents = "";
                        return Page();
                    }

                    if (LooksBinary(bytes))
                    {
                        CanEditContents = false;
                        IsBinary = true;
                        IsDirectory = false;
                        Input.IsDirectory = false;
                        Input.Contents = "";
                        return Page();
                    }

                    CanEditContents = true;
                    IsBinary = false;
                    IsDirectory = false;
                    Input.IsDirectory = false;
                    Input.Contents = DecodeUtf8(bytes);
                    return Page();
                }
                catch
                {
                    // Fall through: might be a folder.
                }
            }

            // Folder (rename-only).
            IsDirectory = true;
            Input.IsDirectory = true;
            CanEditContents = false;
            IsBinary = false;
            Input.Contents = "";
            return Page();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostCommitAsync(string repoName, CancellationToken cancellationToken)
    {
        RepoName = repoName;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.OriginalPath))
        {
            return NotFound();
        }

        Path = Normalize(Input.OriginalPath);
        if (Path == "/")
        {
            return BadRequest("Refusing to edit repository root.");
        }
        ParentPath = GetParent(Path);

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

        if (_access.GetAccess(userId.Value, repo.Id, Path) < AccessLevel.Write)
        {
            return Forbid();
        }

        try
        {
            Revision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
        }
        catch
        {
            Revision = 0;
        }

        var newName = (Input.FileName ?? "").Trim();
        if (!IsSafeFileName(newName))
        {
            ModelState.AddModelError(nameof(Input.FileName), "Invalid file name.");
            return Page();
        }

        var newPath = ParentPath == "/" ? "/" + newName : ParentPath + "/" + newName;
        newPath = Normalize(newPath);

        if (_access.GetAccess(userId.Value, repo.Id, newPath) < AccessLevel.Write)
        {
            return Forbid();
        }

        var msg = (Input.CommitMessage ?? "").Trim();
        if (msg.Length == 0)
        {
            ModelState.AddModelError(nameof(Input.CommitMessage), "Commit message is required.");
            return Page();
        }

        try
        {
            if (Input.IsDirectory)
            {
                IsDirectory = true;
                CanEditContents = false;
                Input.Contents = "";

                if (string.Equals(Path, newPath, StringComparison.Ordinal))
                {
                    ModelState.AddModelError(nameof(Input.FileName), "Name unchanged.");
                    return Page();
                }

                await _writer.MoveAsync(repo.LocalPath, Path, newPath, msg, cancellationToken);
                return RedirectToPage("/Repos/Tree", new { repoName, path = newPath });
            }

            IsDirectory = false;
            var canEditContents = LooksTextByFileName(Path) && LooksTextByFileName(newPath);
            if (!canEditContents)
            {
                IsBinary = true;
                CanEditContents = false;
                Input.Contents = "";

                if (string.Equals(Path, newPath, StringComparison.Ordinal))
                {
                    ModelState.AddModelError(string.Empty, "This file is binary; only rename is supported.");
                    return Page();
                }

                await _writer.MoveAsync(repo.LocalPath, Path, newPath, msg, cancellationToken);
                return RedirectToPage("/Repos/File", new { repoName, path = newPath });
            }

            IsBinary = false;
            CanEditContents = true;
            var bytes = Encoding.UTF8.GetBytes(Input.Contents ?? "");
            await _writer.PutFileAsync(repo.LocalPath, Path, newPath, bytes, msg, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return Page();
        }

        return RedirectToPage("/Repos/File", new { repoName, path = newPath });
    }

    public sealed class EditInput
    {
        [Required]
        public string OriginalPath { get; set; } = "";

        public bool IsDirectory { get; set; }

        [Required]
        [Display(Name = "File name")]
        public string FileName { get; set; } = "";

        [Display(Name = "Contents")]
        public string Contents { get; set; } = "";

        [Required]
        [Display(Name = "Commit message")]
        public string CommitMessage { get; set; } = "";
    }

    private static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return false;
        }

        if (fileName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return fileName.Trim().Length != 0;
    }

    private static bool LooksBinary(byte[] bytes)
    {
        // Quick heuristic: NUL byte is a strong indicator of binary content.
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

    private static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool LooksTextByFileName(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        // Treat common text media types as editable; everything else is "binary/unsupported".
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

        // Extra extensions that are typically plain text but not always mapped by FileExtensionContentTypeProvider.
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext is
            ".md" or ".txt" or ".log" or ".ini" or ".cfg" or ".config" or ".yml" or ".yaml" or
            ".cs" or ".csproj" or ".sln" or ".props" or ".targets" or ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or
            ".c" or ".h" or ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" or ".hxx" or
            ".v" or ".vh" or ".sv" or ".svh";
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
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
