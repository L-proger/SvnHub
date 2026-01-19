using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

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
    public bool IsNew { get; private set; }
    public string EditorLineNumbers { get; private set; } = "";

    [BindProperty]
    public EditInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetNewAsync(
        string repoName,
        string? parentPath,
        bool isDirectory,
        CancellationToken cancellationToken)
    {
        RepoName = repoName;
        Path = Normalize(parentPath);
        ParentPath = GetParent(Path);
        IsNew = true;

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

        IsDirectory = isDirectory;
        CanEditContents = !isDirectory;
        IsBinary = false;
        EditorLineNumbers = CanEditContents ? LineNumberHelper.Build("") : "";

        Input.IsNew = true;
        Input.ParentPath = Path;
        Input.OriginalPath = Path;
        Input.IsDirectory = isDirectory;
        Input.FileName = isDirectory ? "new-folder" : "new-file.txt";
        Input.CommitMessage = "";
        Input.CanEditContents = !isDirectory;
        Input.Contents = isDirectory ? "" : "";
        Input.Properties = [];
        EnsureNewPropertyRow(Input.Properties);

        return Page();
    }

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
            IsNew = false;
            Input.IsNew = false;
            Input.ParentPath = ParentPath;
            Input.OriginalPath = Path;
            Input.FileName = GetBaseName(Path);
            Input.CommitMessage = "";
            Input.CanEditContents = false;
            Input.Properties = [];

            var props = await TryLoadPropertiesAsync(repo.LocalPath, Path, Revision, cancellationToken);
            Input.Properties = props
                .Select(p => new PropertyInput
                {
                    Name = p.Name,
                    Value = p.Value,
                    IsExisting = true,
                    Delete = false,
                })
                .ToList();
            EnsureNewPropertyRow(Input.Properties);

            IsDirectory = await LooksLikeDirectoryAsync(repo.LocalPath, Path, ParentPath, Revision, cancellationToken);
            Input.IsDirectory = IsDirectory;

            if (IsDirectory)
            {
                CanEditContents = false;
                IsBinary = false;
                EditorLineNumbers = "";
                Input.Contents = "";
                Input.CanEditContents = false;
                return Page();
            }

            // File: allow content editing only when it looks like text (by filename + binary sniff + size).
            if (LooksTextByFileName(Path))
            {
                var bytes = await _svnlook.CatBytesAsync(repo.LocalPath, Path, Revision, cancellationToken);
                if (bytes.Length > MaxEditBytes)
                {
                    CanEditContents = false;
                    IsBinary = true;
                    Input.Contents = "";
                    Input.CanEditContents = false;
                    return Page();
                }

                if (LooksBinary(bytes))
                {
                    CanEditContents = false;
                    IsBinary = true;
                    Input.Contents = "";
                    Input.CanEditContents = false;
                    return Page();
                }

                CanEditContents = true;
                IsBinary = false;
                Input.Contents = DecodeUtf8(bytes);
                Input.CanEditContents = true;
                EditorLineNumbers = LineNumberHelper.Build(Input.Contents);
                return Page();
            }

            // Binary/unrecognized file: rename-only.
            CanEditContents = false;
            IsBinary = true;
            EditorLineNumbers = "";
            Input.Contents = "";
            Input.CanEditContents = false;
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

        IsDirectory = Input.IsDirectory;
        CanEditContents = Input.CanEditContents;
        IsBinary = !CanEditContents && !IsDirectory;
        IsNew = Input.IsNew;
        Input.Properties ??= [];
        EnsureNewPropertyRow(Input.Properties);
        EditorLineNumbers = CanEditContents ? LineNumberHelper.Build(Input.Contents) : "";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (Input.IsNew)
        {
            var parent = Normalize(Input.ParentPath);
            Path = parent;
            ParentPath = GetParent(Path);

            var createUserId = AccessService.GetUserIdFromClaimsPrincipal(User);
            if (createUserId is null)
            {
                return Forbid();
            }

            var createRepo = _repos.FindByName(repoName);
            if (createRepo is null || createRepo.IsArchived)
            {
                return NotFound();
            }

            if (_access.GetAccess(createUserId.Value, createRepo.Id, Path) < AccessLevel.Write)
            {
                return Forbid();
            }

            try
            {
                Revision = await _svnlook.GetYoungestRevisionAsync(createRepo.LocalPath, cancellationToken);
            }
            catch
            {
                Revision = 0;
            }

            var createNewName = (Input.FileName ?? "").Trim();
            if (!IsSafeFileName(createNewName))
            {
                ModelState.AddModelError(nameof(Input.FileName), "Invalid name.");
                return Page();
            }

            var createNewPath = Path == "/" ? "/" + createNewName : Path + "/" + createNewName;
            createNewPath = Normalize(createNewPath);

            if (_access.GetAccess(createUserId.Value, createRepo.Id, createNewPath) < AccessLevel.Write)
            {
                return Forbid();
            }

            var createMsg = (Input.CommitMessage ?? "").Trim();
            if (createMsg.Length == 0)
            {
                ModelState.AddModelError(nameof(Input.CommitMessage), "Commit message is required.");
                return Page();
            }

            var createEdits = BuildPropertyEdits(Input.Properties, ModelState);
            if (!ModelState.IsValid)
            {
                return Page();
            }

            try
            {
                // Reject creating over an existing entry (best-effort).
                if (Revision > 0)
                {
                    var siblings = await _svnlook.ListTreeAsync(createRepo.LocalPath, Path, Revision, cancellationToken);
                    if (siblings.Any(e => string.Equals(e.Path, createNewPath, StringComparison.Ordinal)))
                    {
                        ModelState.AddModelError(nameof(Input.FileName), "An entry with this name already exists.");
                        return Page();
                    }
                }
            }
            catch
            {
                // ignore best-effort existence check
            }

            try
            {
                if (Input.IsDirectory)
                {
                    await _writer.CreateDirectoryAsync(
                        createRepo.LocalPath,
                        createNewPath,
                        createEdits,
                        createMsg,
                        User?.Identity?.Name,
                        cancellationToken);
                    return RedirectToPage("/Repos/Tree", new { repoName, path = createNewPath });
                }

                var createContentBytes = Encoding.UTF8.GetBytes(Input.Contents ?? "");
                if (createContentBytes.Length > MaxEditBytes)
                {
                    ModelState.AddModelError(nameof(Input.Contents), $"File is too large (>{MaxEditBytes} bytes).");
                    return Page();
                }

                // For create: oldPath == newPath to avoid svnmucc mv.
                await _writer.EditAsync(
                    createRepo.LocalPath,
                    createNewPath,
                    createNewPath,
                    createContentBytes,
                    createEdits,
                    createMsg,
                    User?.Identity?.Name,
                    cancellationToken);
                return RedirectToPage("/Repos/File", new { repoName, path = createNewPath });
            }
            catch (Exception ex)
            {
                FlashError = ex.Message;
                return Page();
            }
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

        var edits = BuildPropertyEdits(Input.Properties, ModelState);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        byte[]? contentBytes = null;
        if (!Input.IsDirectory && Input.CanEditContents)
        {
            contentBytes = Encoding.UTF8.GetBytes(Input.Contents ?? "");
            if (contentBytes.Length > MaxEditBytes)
            {
                ModelState.AddModelError(nameof(Input.Contents), $"File is too large (>{MaxEditBytes} bytes).");
                return Page();
            }
        }

        var anyAction =
            !string.Equals(Path, newPath, StringComparison.Ordinal) ||
            contentBytes is not null ||
            edits.Count != 0;

        if (!anyAction)
        {
            ModelState.AddModelError(string.Empty, "No changes to commit.");
            return Page();
        }

        try
        {
            await _writer.EditAsync(repo.LocalPath, Path, newPath, contentBytes, edits, msg, User?.Identity?.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return Page();
        }

        if (Input.IsDirectory)
        {
            return RedirectToPage("/Repos/Tree", new { repoName, path = newPath });
        }

        return RedirectToPage("/Repos/File", new { repoName, path = newPath });
    }

    public sealed class EditInput
    {
        [Required]
        public string OriginalPath { get; set; } = "";

        public bool IsNew { get; set; }

        [Required]
        public string ParentPath { get; set; } = "/";

        public bool IsDirectory { get; set; }

        [Required]
        [Display(Name = "Name")]
        public string FileName { get; set; } = "";

        [Display(Name = "Contents")]
        public string? Contents { get; set; }

        public bool CanEditContents { get; set; }

        public List<PropertyInput> Properties { get; set; } = [];

        [Required]
        [Display(Name = "Commit message")]
        public string CommitMessage { get; set; } = "";
    }

    public sealed class PropertyInput
    {
        public string? Name { get; set; }

        public string? Value { get; set; }

        public bool Delete { get; set; }

        public bool IsExisting { get; set; }
    }

    private static IReadOnlyList<SvnPropertyEdit> BuildPropertyEdits(
        IReadOnlyList<PropertyInput> inputs,
        ModelStateDictionary modelState)
    {
        var edits = new List<SvnPropertyEdit>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in inputs)
        {
            var name = (p.Name ?? "").Trim();
            var value = p.Value ?? "";

            if (name.Length == 0)
            {
                if (p.Delete || !string.IsNullOrWhiteSpace(value))
                {
                    modelState.AddModelError(nameof(EditInput.Properties), "Property name is required when value/delete is set.");
                }
                continue;
            }

            if (!IsSafePropertyName(name))
            {
                modelState.AddModelError(nameof(EditInput.Properties), $"Invalid property name: {name}");
                continue;
            }

            if (!seen.Add(name))
            {
                modelState.AddModelError(nameof(EditInput.Properties), $"Duplicate property name: {name}");
                continue;
            }

            if (p.Delete)
            {
                if (!p.IsExisting)
                {
                    modelState.AddModelError(nameof(EditInput.Properties), $"Cannot delete a new property: {name}");
                    continue;
                }

                edits.Add(SvnPropertyEdit.Delete(name));
            }
            else
            {
                edits.Add(SvnPropertyEdit.Set(name, value));
            }
        }

        return edits;
    }

    private static bool IsSafePropertyName(string name)
    {
        // Conservative validation: allow typical svn:* and custom namespaces.
        // Disallow whitespace and path separators.
        if (name.Any(char.IsWhiteSpace))
        {
            return false;
        }

        return !name.Contains('/') && !name.Contains('\\');
    }

    private async Task<IReadOnlyList<SvnProperty>> TryLoadPropertiesAsync(
        string repoLocalPath,
        string path,
        long revision,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _svnlook.GetPropertiesAsync(repoLocalPath, path, revision, cancellationToken);
        }
        catch
        {
            return Array.Empty<SvnProperty>();
        }
    }

    private async Task<bool> LooksLikeDirectoryAsync(
        string repoLocalPath,
        string path,
        string parentPath,
        long revision,
        CancellationToken cancellationToken)
    {
        try
        {
            var entries = await _svnlook.ListTreeAsync(repoLocalPath, parentPath, revision, cancellationToken);
            return entries.Any(e => string.Equals(e.Path, path, StringComparison.Ordinal) && e.IsDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureNewPropertyRow(List<PropertyInput> props)
    {
        if (props.Any(p => !p.IsExisting && string.IsNullOrWhiteSpace(p.Name)))
        {
            return;
        }

        props.Add(new PropertyInput { Name = "", Value = "", Delete = false, IsExisting = false });
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

    private static string GetBaseName(string path)
    {
        var p = path.Replace('\\', '/').TrimEnd('/');
        var idx = p.LastIndexOf('/');
        return idx < 0 ? p : p[(idx + 1)..];
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
