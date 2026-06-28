using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class FileModel : PageModel
{
    private const int MaxChars = 1_000_000;

    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly ISvnRepositoryWriter _svnWriter;
    private readonly SettingsService _settings;
    private readonly SvnHubOptions _options;
    private readonly AltiumPreviewRenderer _altiumPreview;

    public FileModel(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        ISvnRepositoryWriter svnWriter,
        SettingsService settings,
        SvnHubOptions options,
        AltiumPreviewRenderer altiumPreview)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _svnWriter = svnWriter;
        _settings = settings;
        _options = options;
        _altiumPreview = altiumPreview;
    }

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashError { get; set; }

    public string RepoName { get; private set; } = "";
    public string Path { get; private set; } = "/";
    public string ParentPath { get; private set; } = "/";
    public long HeadRevision { get; private set; }
    public long Revision { get; private set; }
    public long? ViewRevision { get; private set; }
    public string Contents { get; private set; } = "";
    public bool IsTruncated { get; private set; }
    public string? Error { get; private set; }
    public string HighlightedHtml { get; private set; } = "";
    public bool IsMarkdown { get; private set; }
    public string MarkdownHtml { get; private set; } = "";
    public bool IsImage { get; private set; }
    public bool IsPdf { get; private set; }
    public bool IsGerberPreview { get; private set; }
    public bool IsModelPreview { get; private set; }
    public bool IsAltiumPreview { get; private set; }
    public bool IsAltiumPcbDocument { get; private set; }
    public string AltiumPreviewLabel { get; private set; } = "";
    public string ImageContentType { get; private set; } = "application/octet-stream";
    public string Language { get; private set; } = "plaintext";
    public string LineNumbers { get; private set; } = "";
    public long FileSizeBytes { get; private set; }
    public string FileSizeLabel { get; private set; } = "";
    public IReadOnlyList<GerberPreviewFile> GerberPreviewFiles { get; private set; } = [];
    public IReadOnlyList<ModelPreviewFile> ModelPreviewFiles { get; private set; } = [];
    public int? LineCount { get; private set; }
    public bool CanWrite { get; private set; }
    public string? CheckoutUrl { get; private set; }
    public bool CanEdit { get; private set; }
    public bool CanServeFile { get; private set; }
    public string MaxPreviewSizeLabel { get; private set; } = "";
    public string? PreviewUnavailableMessage { get; private set; }
    public bool CanCopyContent =>
        Error is null &&
        PreviewUnavailableMessage is null &&
        !IsImage &&
        !IsPdf &&
        !IsGerberPreview &&
        !IsModelPreview &&
        !IsAltiumPreview &&
        (IsMarkdown || LineCount is not null);

    public async Task<IActionResult> OnGetAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);
        ParentPath = GetParent(Path);
        CheckoutUrl = SvnCheckoutUrl.Build(_settings.GetEffectiveSvnBaseUrl(), repoName, Path);
        var language = RepositoryFileClassifier.GuessLanguage(Path);
        Language = language;
        IsImage = RepositoryFileClassifier.IsImagePath(Path);
        var isPdfPath = RepositoryFileClassifier.IsPdfPath(Path);
        ImageContentType = RepositoryFileClassifier.GetContentTypeOrDefault(Path);

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

        CanWrite = _access.GetAccess(userId.Value, repo.Id, Path) >= AccessLevel.Write;
        ViewRevision = rev;

        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            Revision = ResolveRevision(rev, HeadRevision);
            CanEdit = CanWrite && rev is null;
            var maxPreviewBytes = _options.GetEffectiveMaxPreviewBytes();
            MaxPreviewSizeLabel = FormatByteSize(maxPreviewBytes);
            try
            {
                FileSizeBytes = await _svnlook.GetFileSizeAsync(repo.LocalPath, Path, Revision, cancellationToken);
                FileSizeLabel = FormatByteSize(FileSizeBytes);
                CanServeFile = FileSizeBytes <= maxPreviewBytes;
            }
            catch (Exception ex)
            {
                FileSizeBytes = 0;
                FileSizeLabel = "";
                CanServeFile = false;
                PreviewUnavailableMessage = $"Preview is disabled because file size could not be determined: {ex.Message}";
                return Page();
            }

            if (!CanServeFile)
            {
                PreviewUnavailableMessage =
                    $"Preview is disabled for files larger than {MaxPreviewSizeLabel}. This file is {FileSizeLabel}.";
                return Page();
            }

            if (GerberDrillFileClassifier.IsBoardFileCandidate(Path))
            {
                GerberPreviewFiles = await BuildGerberPreviewFilesAsync(
                    repo.Id,
                    repo.LocalPath,
                    userId.Value,
                    Revision,
                    maxPreviewBytes,
                    cancellationToken);

                if (GerberPreviewFiles.Count != 0)
                {
                    ClearTextPreviewState();
                    IsGerberPreview = true;
                    return Page();
                }

                if (!string.IsNullOrWhiteSpace(PreviewUnavailableMessage))
                {
                    ClearTextPreviewState();
                    return Page();
                }
            }

            if (ModelPreviewFileClassifier.IsImportableModelPath(Path))
            {
                ModelPreviewFiles = await BuildModelPreviewFilesAsync(
                    repo.Id,
                    repo.LocalPath,
                    userId.Value,
                    Revision,
                    maxPreviewBytes,
                    cancellationToken);

                if (ModelPreviewFiles.Count != 0)
                {
                    ClearTextPreviewState();
                    IsModelPreview = true;
                    return Page();
                }

                if (!string.IsNullOrWhiteSpace(PreviewUnavailableMessage))
                {
                    ClearTextPreviewState();
                    return Page();
                }
            }

            if (AltiumPreviewFileClassifier.IsPreviewablePath(Path))
            {
                var altiumKind = AltiumPreviewFileClassifier.GetKind(Path);
                ClearTextPreviewState();
                IsAltiumPreview = true;
                IsAltiumPcbDocument = altiumKind == AltiumPreviewKind.PcbDocument;
                AltiumPreviewLabel = AltiumPreviewFileClassifier.Describe(altiumKind);
                return Page();
            }

            if (IsImage)
            {
                // Render via Raw handler (binary); don't try to read it as text.
                ClearTextPreviewState();
                return Page();
            }

            if (isPdfPath)
            {
                var pdfSniffBytes = await _svnlook.CatPrefixBytesAsync(
                    repo.LocalPath,
                    Path,
                    Revision,
                    RepositoryFileClassifier.SniffByteCount,
                    cancellationToken);

                if (RepositoryFileClassifier.LooksPdfContent(pdfSniffBytes))
                {
                    IsPdf = true;
                    ClearTextPreviewState();
                    return Page();
                }
            }

            var sniffBytes = await _svnlook.CatPrefixBytesAsync(
                repo.LocalPath,
                Path,
                Revision,
                RepositoryFileClassifier.SniffByteCount,
                cancellationToken);

            if (RepositoryFileClassifier.LooksBinary(sniffBytes))
            {
                PreviewUnavailableMessage = "No preview is available because this file appears to be binary.";
                return Page();
            }

            var bytes = FileSizeBytes <= sniffBytes.Length
                ? sniffBytes
                : await _svnlook.CatBytesAsync(repo.LocalPath, Path, Revision, cancellationToken);

            var content = RepositoryFileClassifier.DecodeText(bytes);
            if (content.Length > MaxChars)
            {
                Contents = content[..MaxChars];
                IsTruncated = true;
            }
            else
            {
                Contents = content;
            }

            // Even if content is truncated, allow opening the editor (rename-only is still useful).

            IsMarkdown = string.Equals(language, "markdown", StringComparison.Ordinal);
            if (IsMarkdown)
            {
                LineNumbers = "";
                LineCount = null;
                MarkdownHtml = MarkdownRenderer.Render(Contents, repoName, Path, rev);
                HighlightedHtml = "";
            }
            else
            {
                HighlightedHtml = "";
                LineNumbers = LineNumberHelper.Build(Contents);
                LineCount = LineNumberHelper.CountLines(Contents);
                MarkdownHtml = "";
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        return Page();
    }

    private void ClearTextPreviewState()
    {
        Contents = "";
        HighlightedHtml = "";
        MarkdownHtml = "";
        IsMarkdown = false;
        IsGerberPreview = false;
        IsModelPreview = false;
        IsAltiumPreview = false;
        IsAltiumPcbDocument = false;
        AltiumPreviewLabel = "";
        LineNumbers = "";
        LineCount = null;
    }

    private async Task<IReadOnlyList<GerberPreviewFile>> BuildGerberPreviewFilesAsync(
        Guid repoId,
        string repoLocalPath,
        Guid userId,
        long revision,
        long maxPreviewBytes,
        CancellationToken cancellationToken)
    {
        var entries = await _svnlook.ListTreeAsync(repoLocalPath, ParentPath, revision, cancellationToken);
        var candidates = entries
            .Where(e => !e.IsDirectory)
            .Where(e => GerberDrillFileClassifier.IsBoardFileCandidate(e.Path))
            .Where(e => _access.GetAccess(userId, repoId, e.Path) >= AccessLevel.Read)
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        var files = new List<GerberPreviewFile>(candidates.Length);
        long totalBytes = 0;
        foreach (var entry in candidates)
        {
            var size = await _svnlook.GetFileSizeAsync(repoLocalPath, entry.Path, revision, cancellationToken);
            if (size > maxPreviewBytes)
            {
                PreviewUnavailableMessage =
                    $"Gerber preview is disabled because {entry.Name} is larger than {MaxPreviewSizeLabel}.";
                return [];
            }

            totalBytes += size;
            if (totalBytes > maxPreviewBytes)
            {
                PreviewUnavailableMessage =
                    $"Gerber preview is disabled because the CAM file set is larger than {MaxPreviewSizeLabel}.";
                return [];
            }

            files.Add(new GerberPreviewFile(
                entry.Name,
                entry.Path,
                GerberDrillFileClassifier.Describe(entry.Path),
                size,
                FormatByteSize(size)));
        }

        return files;
    }

    private async Task<IReadOnlyList<ModelPreviewFile>> BuildModelPreviewFilesAsync(
        Guid repoId,
        string repoLocalPath,
        Guid userId,
        long revision,
        long maxPreviewBytes,
        CancellationToken cancellationToken)
    {
        var entries = await _svnlook.ListTreeAsync(repoLocalPath, ParentPath, revision, cancellationToken);
        var candidates = entries
            .Where(e => !e.IsDirectory)
            .Where(e => ModelPreviewFileClassifier.IsRelatedModelPath(e.Path))
            .Where(e => _access.GetAccess(userId, repoId, e.Path) >= AccessLevel.Read)
            .OrderByDescending(e => string.Equals(e.Path, Path, StringComparison.Ordinal))
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Take(96)
            .ToArray();

        if (candidates.Length == 0)
        {
            return [];
        }

        var files = new List<ModelPreviewFile>(candidates.Length);
        long totalBytes = 0;
        foreach (var entry in candidates)
        {
            var size = await _svnlook.GetFileSizeAsync(repoLocalPath, entry.Path, revision, cancellationToken);
            if (size > maxPreviewBytes)
            {
                PreviewUnavailableMessage =
                    $"3D preview is disabled because {entry.Name} is larger than {MaxPreviewSizeLabel}.";
                return [];
            }

            totalBytes += size;
            if (totalBytes > maxPreviewBytes)
            {
                PreviewUnavailableMessage =
                    $"3D preview is disabled because the model file set is larger than {MaxPreviewSizeLabel}.";
                return [];
            }

            files.Add(new ModelPreviewFile(
                entry.Name,
                entry.Path,
                ModelPreviewFileClassifier.Describe(entry.Path),
                size,
                FormatByteSize(size)));
        }

        return files;
    }

    private static string FormatByteSize(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        if (unit == 0)
        {
            return $"{bytes} {units[unit]}";
        }

        return string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string repoName, string? path, CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);
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

        var actor = User?.Identity?.Name ?? userId.Value.ToString("D");
        var message = $"Delete {Path} via SvnHub (by {actor})";

        try
        {
            await _svnWriter.DeleteAsync(repo.LocalPath, Path, message, User?.Identity?.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            FlashError = ex.Message;
            return RedirectToPage("/Repos/File", new { repoName, path = RepositoryPath.ToRouteValue(Path) });
        }

        TempData["Message"] = $"Deleted {System.IO.Path.GetFileName(Path)}";
        return RedirectToPage("/Repos/Tree", new { repoName, path = RepositoryPath.ToRouteValue(ParentPath) });
    }

    public async Task<IActionResult> OnGetAltiumPreviewAsync(
        string repoName,
        string? path,
        long? rev,
        string? side,
        CancellationToken cancellationToken)
    {
        RepoName = repoName;
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        Path = Normalize(path);
        if (!AltiumPreviewFileClassifier.IsPreviewablePath(Path))
        {
            return NotFound();
        }

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
            var headRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            var effectiveRev = ResolveRevision(rev, headRevision);
            var maxServeBytes = _options.GetEffectiveMaxPreviewBytes();
            var fileSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
            if (fileSize > maxServeBytes)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    BuildFileTooLargeMessage(fileSize, maxServeBytes));
            }

            var bytes = await _svnlook.CatBytesAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
            var previewSide = string.Equals(side, "bottom", StringComparison.OrdinalIgnoreCase)
                ? AltiumPreviewSide.Bottom
                : AltiumPreviewSide.Top;
            var svg = await _altiumPreview.RenderSvgAsync(bytes, Path, previewSide, cancellationToken);

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return Content(svg, "image/svg+xml; charset=utf-8");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest($"Altium preview failed: {ex.Message}");
        }
    }

    public async Task<IActionResult> OnGetDownloadAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken)
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

        long effectiveRev;
        byte[] content;
        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            effectiveRev = ResolveRevision(rev, HeadRevision);
            var maxServeBytes = _options.GetEffectiveMaxPreviewBytes();
            var fileSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
            if (fileSize > maxServeBytes)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    BuildFileTooLargeMessage(fileSize, maxServeBytes));
            }

            content = await _svnlook.CatBytesAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        var fileName = System.IO.Path.GetFileName(Path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "download";
        }

        var contentType = RepositoryFileClassifier.GetContentTypeOrDefault(fileName);

        Response.Headers.ETag = $"W/\"{repoName}:{effectiveRev}:{Path}\"";
        return File(content, contentType, fileName);
    }

    public async Task<IActionResult> OnGetRawAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken)
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

        long effectiveRev;
        byte[] content;
        try
        {
            HeadRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            effectiveRev = ResolveRevision(rev, HeadRevision);
            var maxServeBytes = _options.GetEffectiveMaxPreviewBytes();
            var fileSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
            if (fileSize > maxServeBytes)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    BuildFileTooLargeMessage(fileSize, maxServeBytes));
            }

            content = await _svnlook.CatBytesAsync(repo.LocalPath, Path, effectiveRev, cancellationToken);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        var fileName = System.IO.Path.GetFileName(Path);
        var contentType = RepositoryFileClassifier.GetContentTypeOrDefault(fileName);
        contentType = RepositoryFileClassifier.NormalizeRawTextContentType(fileName, contentType, content);

        Response.Headers.ETag = $"W/\"{repoName}:{effectiveRev}:{Path}\"";
        return File(content, contentType);
    }

    private static string BuildFileTooLargeMessage(long fileSize, long maxServeBytes) =>
        $"File is too large to serve through the SvnHub browser ({FormatByteSize(fileSize)} > {FormatByteSize(maxServeBytes)}). " +
        "Use SVN checkout or the repository SVN URL instead.";

    private static long ResolveRevision(long? requested, long head)
    {
        if (requested is null)
        {
            return head;
        }

        if (requested.Value <= 0 || requested.Value > head)
        {
            throw new InvalidOperationException($"Invalid revision: r{requested.Value}.");
        }

        return requested.Value;
    }

    private static string Normalize(string? path) => RepositoryPath.Normalize(path);

    private static string GetParent(string path) => RepositoryPath.GetParent(path);
}
