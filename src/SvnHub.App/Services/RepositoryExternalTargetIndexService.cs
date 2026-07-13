using System.Security.Cryptography;
using System.Text;
using SvnHub.App.Indexing;
using SvnHub.App.Storage;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class RepositoryExternalTargetIndexService
{
    private readonly IPortalStore _portalStore;
    private readonly IRepositoryIndexStore _indexStore;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);

    public RepositoryExternalTargetIndexService(
        IPortalStore portalStore,
        IRepositoryIndexStore indexStore,
        SettingsService settings)
    {
        _portalStore = portalStore;
        _indexStore = indexStore;
        _settings = settings;
    }

    public SvnExternalTargetResolutionContext CreateResolutionContext()
    {
        var state = _portalStore.Read();
        return CreateResolutionContext(state);
    }

    public async Task EnsureCurrentAsync(CancellationToken cancellationToken = default)
    {
        var context = CreateResolutionContext();
        var currentSignature = await _indexStore.GetExternalTargetIndexSignatureAsync(cancellationToken);
        if (string.Equals(currentSignature, context.Signature, StringComparison.Ordinal))
        {
            return;
        }

        await _rebuildGate.WaitAsync(cancellationToken);
        try
        {
            context = CreateResolutionContext();
            currentSignature = await _indexStore.GetExternalTargetIndexSignatureAsync(cancellationToken);
            if (string.Equals(currentSignature, context.Signature, StringComparison.Ordinal))
            {
                return;
            }

            var externals = await _indexStore.ListHeadExternalsAsync(cancellationToken);
            var updates = new List<RepositoryIndexExternalTargetUpdate>(externals.Count);
            foreach (var external in externals)
            {
                var target = context.Resolve(
                    external.RepositoryName,
                    external.ParentPath,
                    external.Url);
                if (target is null)
                {
                    continue;
                }

                updates.Add(new RepositoryIndexExternalTargetUpdate(
                    external.RepositoryId,
                    external.Ordinal,
                    target.RepositoryId,
                    target.Path));
            }

            await _indexStore.RebuildExternalTargetsAsync(
                context.Signature,
                updates,
                cancellationToken);
        }
        finally
        {
            _rebuildGate.Release();
        }
    }

    private SvnExternalTargetResolutionContext CreateResolutionContext(PortalState state)
    {
        var repositories = state.Repositories
            .Where(repository => repository.IsAvailable)
            .OrderBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(repository => repository.Id)
            .ToArray();
        var repositoriesByName = repositories.ToDictionary(
            repository => repository.Name,
            StringComparer.OrdinalIgnoreCase);
        var svnBaseUrls = _settings.GetEffectiveSvnBaseUrls(state);

        var signatureSource = new StringBuilder();
        foreach (var repository in repositories)
        {
            signatureSource
                .Append(repository.Id.ToString("N"))
                .Append(':')
                .Append(repository.Name)
                .Append('\n');
        }

        signatureSource.Append("--urls--\n");
        foreach (var svnBaseUrl in svnBaseUrls)
        {
            signatureSource.Append(svnBaseUrl).Append('\n');
        }

        var signature = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(signatureSource.ToString())));
        return new SvnExternalTargetResolutionContext(
            signature,
            repositoriesByName,
            svnBaseUrls);
    }
}

public sealed record SvnExternalTargetResolutionContext(
    string Signature,
    IReadOnlyDictionary<string, Repository> RepositoriesByName,
    IReadOnlyList<string> SvnBaseUrls)
{
    public SvnExternalTarget? Resolve(
        string sourceRepositoryName,
        string sourceParentPath,
        string? externalUrl) =>
        SvnExternalTargetResolver.Resolve(
            sourceRepositoryName,
            sourceParentPath,
            RepositoriesByName,
            SvnBaseUrls,
            externalUrl);
}
