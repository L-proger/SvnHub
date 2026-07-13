using SvnHub.Domain;

namespace SvnHub.App.System;

public static class SvnExternalTargetResolver
{
    public static SvnExternalTarget? Resolve(
        string sourceRepositoryName,
        string sourceParentPath,
        IReadOnlyCollection<Repository> repositories,
        IReadOnlyList<string> svnBaseUrls,
        string? externalUrl)
    {
        var repositoriesByName = repositories.ToDictionary(
            repository => repository.Name,
            StringComparer.OrdinalIgnoreCase);
        return Resolve(
            sourceRepositoryName,
            sourceParentPath,
            repositoriesByName,
            svnBaseUrls,
            externalUrl);
    }

    public static SvnExternalTarget? Resolve(
        string sourceRepositoryName,
        string sourceParentPath,
        IReadOnlyDictionary<string, Repository> repositoriesByName,
        IReadOnlyList<string> svnBaseUrls,
        string? externalUrl)
    {
        if (string.IsNullOrWhiteSpace(externalUrl))
        {
            return null;
        }

        var value = externalUrl.Trim().Replace('\\', '/');
        if (value.StartsWith("^/", StringComparison.Ordinal))
        {
            return ResolveRelative(
                [sourceRepositoryName],
                value[2..],
                repositoriesByName);
        }

        if (value.StartsWith("../", StringComparison.Ordinal) ||
            value.StartsWith("./", StringComparison.Ordinal))
        {
            var sourceSegments = new List<string> { sourceRepositoryName };
            sourceSegments.AddRange(SplitPath(sourceParentPath));
            return ResolveRelative(sourceSegments, value, repositoriesByName);
        }

        if (value.StartsWith("//", StringComparison.Ordinal))
        {
            foreach (var baseUrl in svnBaseUrls)
            {
                if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
                    Uri.TryCreate($"{baseUri.Scheme}:{value}", UriKind.Absolute, out var absoluteUri))
                {
                    var target = ResolveAbsolute(absoluteUri, repositoriesByName, svnBaseUrls);
                    if (target is not null)
                    {
                        return target;
                    }
                }
            }

            return null;
        }

        if (value.StartsWith("/", StringComparison.Ordinal))
        {
            foreach (var baseUrl in svnBaseUrls)
            {
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) ||
                    !Uri.TryCreate(new Uri(baseUri.GetLeftPart(UriPartial.Authority)), value, out var absoluteUri))
                {
                    continue;
                }

                var target = ResolveAbsolute(absoluteUri, repositoriesByName, svnBaseUrls);
                if (target is not null)
                {
                    return target;
                }
            }

            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? ResolveAbsolute(uri, repositoriesByName, svnBaseUrls)
            : null;
    }

    private static SvnExternalTarget? ResolveAbsolute(
        Uri externalUri,
        IReadOnlyDictionary<string, Repository> repositoriesByName,
        IReadOnlyList<string> svnBaseUrls)
    {
        foreach (var baseUrl in svnBaseUrls)
        {
            if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
                !string.Equals(externalUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(externalUri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var basePath = baseUri.AbsolutePath.TrimEnd('/');
            var externalPath = externalUri.AbsolutePath;
            if (!externalPath.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Uri.UnescapeDataString(externalPath[(basePath.Length + 1)..]);
            return ResolveRelative([], relativePath, repositoriesByName);
        }

        return null;
    }

    private static SvnExternalTarget? ResolveRelative(
        IReadOnlyCollection<string> baseSegments,
        string relativePath,
        IReadOnlyDictionary<string, Repository> repositoriesByName)
    {
        var segments = new List<string>(baseSegments);
        foreach (var rawSegment in SplitPath(Uri.UnescapeDataString(relativePath)))
        {
            if (rawSegment == ".")
            {
                continue;
            }

            if (rawSegment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }

                continue;
            }

            segments.Add(rawSegment);
        }

        if (segments.Count == 0)
        {
            return null;
        }

        if (!repositoriesByName.TryGetValue(segments[0], out var repository))
        {
            return null;
        }

        var path = segments.Count == 1
            ? "/"
            : "/" + string.Join('/', segments.Skip(1));
        return new SvnExternalTarget(
            repository.Id,
            repository.Name,
            SvnExternalDefinitionParser.NormalizeRepositoryPath(path));
    }

    private static IEnumerable<string> SplitPath(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public sealed record SvnExternalTarget(
    Guid RepositoryId,
    string RepositoryName,
    string Path);
