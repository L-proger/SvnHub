using Microsoft.AspNetCore.Mvc;
using SvnHub.App.Configuration;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;
using System.Globalization;
using System.Security.Claims;

namespace SvnHub.Web.Support;

public sealed class RepositoryFileResponseService
{
    private readonly RepositoryService _repos;
    private readonly AccessService _access;
    private readonly ISvnLookClient _svnlook;
    private readonly SvnHubOptions _options;

    public RepositoryFileResponseService(
        RepositoryService repos,
        AccessService access,
        ISvnLookClient svnlook,
        SvnHubOptions options)
    {
        _repos = repos;
        _access = access;
        _svnlook = svnlook;
        _options = options;
    }

    public async Task<IActionResult> ServeRawAsync(
        ClaimsPrincipal user,
        HttpResponse response,
        string repoName,
        string? path,
        long? revision,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new NotFoundResult();
        }

        var normalizedPath = RepositoryPath.Normalize(path);
        var userId = AccessService.GetUserIdFromClaimsPrincipal(user);
        if (userId is null)
        {
            return new ForbidResult();
        }

        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return new NotFoundResult();
        }

        if (_access.GetAccess(userId.Value, repo.Id, normalizedPath) < AccessLevel.Read)
        {
            return new ForbidResult();
        }

        long effectiveRevision;
        byte[] content;
        try
        {
            var headRevision = await _svnlook.GetYoungestRevisionAsync(repo.LocalPath, cancellationToken);
            effectiveRevision = ResolveRevision(revision, headRevision);
            var maxServeBytes = _options.GetEffectiveMaxPreviewBytes();
            var fileSize = await _svnlook.GetFileSizeAsync(repo.LocalPath, normalizedPath, effectiveRevision, cancellationToken);
            if (fileSize > maxServeBytes)
            {
                return new ObjectResult(BuildFileTooLargeMessage(fileSize, maxServeBytes))
                {
                    StatusCode = StatusCodes.Status413PayloadTooLarge,
                };
            }

            content = await _svnlook.CatBytesAsync(repo.LocalPath, normalizedPath, effectiveRevision, cancellationToken);
        }
        catch (Exception ex)
        {
            return new BadRequestObjectResult(ex.Message);
        }

        var fileName = Path.GetFileName(normalizedPath);
        var contentType = RepositoryFileClassifier.GetContentTypeOrDefault(fileName);
        contentType = RepositoryFileClassifier.NormalizeRawTextContentType(fileName, contentType, content);

        response.Headers.ETag = HttpEntityTags.WeakRepositoryFileTag(repoName, effectiveRevision, normalizedPath);
        return new FileContentResult(content, contentType);
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

        return unit == 0
            ? $"{bytes} {units[unit]}"
            : string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1}", size, units[unit]);
    }
}
