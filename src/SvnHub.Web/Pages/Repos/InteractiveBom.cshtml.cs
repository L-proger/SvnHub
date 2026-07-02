using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class InteractiveBomModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly SvnHubOptions _options;
    private readonly AltiumInteractiveBomGenerator _generator;
    private readonly AltiumProjectBomLoader _projectBomLoader;
    private readonly InteractiveBomHtmlBuilder _htmlBuilder;

    public InteractiveBomModel(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        SvnHubOptions options,
        AltiumInteractiveBomGenerator generator,
        AltiumProjectBomLoader projectBomLoader,
        InteractiveBomHtmlBuilder htmlBuilder)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _options = options;
        _generator = generator;
        _projectBomLoader = projectBomLoader;
        _htmlBuilder = htmlBuilder;
    }

    public async Task<IActionResult> OnGetAsync(
        string repoName,
        string? path,
        long? rev,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return NotFound();
        }

        var normalizedPath = RepositoryPath.Normalize(path);
        var isPcbDocument = AltiumPreviewFileClassifier.GetKind(normalizedPath) == AltiumPreviewKind.PcbDocument;
        var isProject = AltiumPreviewFileClassifier.IsProjectPath(normalizedPath);
        if (!isPcbDocument && !isProject)
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

        if (_access.GetAccess(userId.Value, repo.Id, normalizedPath) < Domain.AccessLevel.Read)
        {
            return Forbid();
        }

        try
        {
            var headRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            var effectiveRev = ResolveRevision(rev, headRevision);
            var maxServeBytes = _options.GetEffectiveMaxPreviewBytes();
            var fileSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, normalizedPath, effectiveRev, cancellationToken);
            if (fileSize > maxServeBytes)
            {
                return StatusCode(
                    StatusCodes.Status413PayloadTooLarge,
                    $"File is too large to serve through the SvnHub browser ({FormatByteSize(fileSize)} > {FormatByteSize(maxServeBytes)}).");
            }

            string genericJson;
            string source;
            byte[] sourceBytes;
            string? selectedPcbDocPath = null;

            if (isProject)
            {
                var projectSource = await _projectBomLoader.LoadForProjectAsync(
                    repo.LocalPath,
                    repo.Id,
                    userId.Value,
                    normalizedPath,
                    effectiveRev,
                    maxServeBytes,
                    cancellationToken);

                sourceBytes = projectSource.PcbDocBytes;
                selectedPcbDocPath = projectSource.PcbDocPath;
                genericJson = _generator.GenerateGenericJson(
                    projectSource.PcbDocBytes,
                    Path.GetFileNameWithoutExtension(normalizedPath),
                    projectSource.Rows);
                source = "altium-project";
            }
            else
            {
                sourceBytes = await _svnlook.CatBytesAsync(repo.LocalPath, normalizedPath, effectiveRev, cancellationToken);
                var projectBom = await _projectBomLoader.TryLoadForPcbDocAsync(
                    repo.LocalPath,
                    repo.Id,
                    userId.Value,
                    normalizedPath,
                    effectiveRev,
                    maxServeBytes,
                    cancellationToken);

                genericJson = _generator.GenerateGenericJson(
                    sourceBytes,
                    Path.GetFileNameWithoutExtension(normalizedPath),
                    projectBom?.Rows);
                source = projectBom is null ? "altium-pcbdoc" : "altium-project";
            }

            var html = _htmlBuilder.Build(genericJson);

            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["X-SvnHub-Preview"] = "interactive-bom";
            Response.Headers["X-SvnHub-Preview-Source"] = source;
            Response.Headers["X-SvnHub-Preview-Bytes"] = sourceBytes.Length.ToString(CultureInfo.InvariantCulture);
            Response.Headers["X-SvnHub-Preview-Html-Length"] = html.Length.ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(selectedPcbDocPath))
            {
                Response.Headers["X-SvnHub-Altium-PcbDoc"] = selectedPcbDocPath;
            }

            Response.Headers.CacheControl = "no-store";
            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest($"Interactive BOM generation failed: {ex.Message}");
        }
    }

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

    private static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = Math.Max(bytes, 0);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
    }
}
