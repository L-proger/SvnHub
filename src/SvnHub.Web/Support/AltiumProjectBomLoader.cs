using OriginalCircuit.Altium.Models.Project;
using OriginalCircuit.Altium.Models.Sch;
using OriginalCircuit.Altium.Serialization.Readers;
using SvnHub.App.Services;
using SvnHub.App.System;
using SvnHub.Domain;

namespace SvnHub.Web.Support;

public sealed class AltiumProjectBomLoader
{
    private readonly ISvnLookClient _svnlook;
    private readonly AccessService _access;

    public AltiumProjectBomLoader(ISvnLookClient svnlook, AccessService access)
    {
        _svnlook = svnlook;
        _access = access;
    }

    public async Task<AltiumProjectBom?> TryLoadForPcbDocAsync(
        string repoLocalPath,
        Guid repoId,
        Guid userId,
        string pcbDocPath,
        long revision,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        var normalizedPcbPath = RepositoryPath.Normalize(pcbDocPath);
        await foreach (var projectPath in FindCandidateProjectPathsAsync(
                           repoLocalPath,
                           normalizedPcbPath,
                           revision,
                           cancellationToken))
        {
            if (_access.GetAccess(userId, repoId, projectPath) < AccessLevel.Read)
            {
                continue;
            }

            AltiumProject project;
            try
            {
                project = await ReadProjectAsync(repoLocalPath, projectPath, revision, maxFileBytes, cancellationToken);
            }
            catch
            {
                continue;
            }

            if (!ProjectReferencesPcb(project, projectPath, normalizedPcbPath))
            {
                continue;
            }

            var rows = await LoadProjectBomRowsAsync(
                repoLocalPath,
                repoId,
                userId,
                project,
                projectPath,
                revision,
                maxFileBytes,
                cancellationToken);

            if (rows.Count != 0)
            {
                return new AltiumProjectBom(projectPath, rows);
            }
        }

        return null;
    }

    public async Task<AltiumProjectBomRenderSource> LoadForProjectAsync(
        string repoLocalPath,
        Guid repoId,
        Guid userId,
        string projectPath,
        long revision,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        var normalizedProjectPath = RepositoryPath.Normalize(projectPath);
        if (_access.GetAccess(userId, repoId, normalizedProjectPath) < AccessLevel.Read)
        {
            throw new InvalidOperationException("You do not have read access to this Altium project.");
        }

        var project = await ReadProjectAsync(
            repoLocalPath,
            normalizedProjectPath,
            revision,
            maxFileBytes,
            cancellationToken);

        var pcbDocuments = project.PcbDocuments
            .Select(d => ResolveProjectDocumentPath(normalizedProjectPath, d.DocumentPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (pcbDocuments.Length == 0)
        {
            throw new InvalidOperationException("Altium project does not reference a PCB document.");
        }

        foreach (var pcbDocPath in pcbDocuments)
        {
            if (_access.GetAccess(userId, repoId, pcbDocPath) < AccessLevel.Read)
            {
                continue;
            }

            try
            {
                await EnsureServeableFileAsync(repoLocalPath, pcbDocPath, revision, maxFileBytes, cancellationToken);
                var pcbDocBytes = await _svnlook.CatBytesAsync(repoLocalPath, pcbDocPath, revision, cancellationToken);
                var rows = await LoadProjectBomRowsAsync(
                    repoLocalPath,
                    repoId,
                    userId,
                    project,
                    normalizedProjectPath,
                    revision,
                    maxFileBytes,
                    cancellationToken);

                return new AltiumProjectBomRenderSource(
                    normalizedProjectPath,
                    pcbDocPath,
                    pcbDocBytes,
                    rows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try the next PCB document if this one is missing or cannot be read at the requested revision.
            }
        }

        throw new InvalidOperationException("Altium project does not contain a readable PCB document.");
    }

    private async IAsyncEnumerable<string> FindCandidateProjectPathsAsync(
        string repoLocalPath,
        string pcbDocPath,
        long revision,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var directory = RepositoryPath.GetParent(pcbDocPath);
             ;
             directory = RepositoryPath.GetParent(directory))
        {
            if (!visited.Add(directory))
            {
                yield break;
            }

            IReadOnlyList<SvnTreeEntry> entries;
            try
            {
                entries = await _svnlook.ListTreeAsync(repoLocalPath, directory, revision, cancellationToken);
            }
            catch
            {
                entries = [];
            }

            foreach (var entry in entries
                         .Where(e => !e.IsDirectory)
                         .Where(e => string.Equals(Path.GetExtension(e.Name), ".PrjPcb", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return entry.Path;
            }

            if (directory == "/")
            {
                yield break;
            }
        }
    }

    private async Task<AltiumProject> ReadProjectAsync(
        string repoLocalPath,
        string projectPath,
        long revision,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        await EnsureServeableFileAsync(repoLocalPath, projectPath, revision, maxFileBytes, cancellationToken);
        var bytes = await _svnlook.CatBytesAsync(repoLocalPath, projectPath, revision, cancellationToken);
        using var stream = new MemoryStream(bytes, writable: false);
        var project = new PrjPcbReader().Read(stream);
        project.FilePath = projectPath;

        var structurePath = ChangeExtension(projectPath, ".PrjPcbStructure");
        try
        {
            await EnsureServeableFileAsync(repoLocalPath, structurePath, revision, maxFileBytes, cancellationToken);
            var structureText = await _svnlook.CatAsync(repoLocalPath, structurePath, revision, cancellationToken);
            project.Structure = ProjectStructure.Parse(structureText);
        }
        catch
        {
            // Structure is useful for hierarchy, but the flat project document list is enough for BOM.
        }

        return project;
    }

    private async Task<Dictionary<string, AltiumBomRow>> LoadProjectBomRowsAsync(
        string repoLocalPath,
        Guid repoId,
        Guid userId,
        AltiumProject project,
        string projectPath,
        long revision,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        var rows = new Dictionary<string, AltiumBomRow>(StringComparer.OrdinalIgnoreCase);
        var projectParameters = ReadProjectParameters(project);

        foreach (var document in project.SchematicDocuments)
        {
            var schDocPath = ResolveProjectDocumentPath(projectPath, document.DocumentPath);
            if (schDocPath is null || _access.GetAccess(userId, repoId, schDocPath) < AccessLevel.Read)
            {
                continue;
            }

            SchDocument schDocument;
            try
            {
                await EnsureServeableFileAsync(repoLocalPath, schDocPath, revision, maxFileBytes, cancellationToken);
                var bytes = await _svnlook.CatBytesAsync(repoLocalPath, schDocPath, revision, cancellationToken);
                using var stream = new MemoryStream(bytes, writable: false);
                schDocument = new SchDocReader().Read(stream);
                schDocument.FileName = Path.GetFileName(schDocPath);
                schDocument.FilePath = schDocPath;
            }
            catch
            {
                continue;
            }

            foreach (var component in schDocument.Components.OfType<SchComponent>())
            {
                var row = CreateBomRow(component, projectParameters);
                if (row is not null)
                {
                    rows[row.Designator] = row;
                }
            }
        }

        return rows;
    }

    private static AltiumBomRow? CreateBomRow(
        SchComponent component,
        IReadOnlyDictionary<string, string> projectParameters)
    {
        var parameters = ReadComponentParameters(component);
        var designator = ReadDesignator(component, parameters);
        if (string.IsNullOrWhiteSpace(designator))
        {
            return null;
        }

        var description = Lookup(parameters, "Description") ?? component.Description ?? "";
        var value = Lookup(parameters, "Comment")
            ?? component.Comment
            ?? Lookup(parameters, "Value")
            ?? description;
        var footprint = ReadFootprint(component);

        AddIfNotEmpty(parameters, "Designator", designator);
        AddIfNotEmpty(parameters, "Comment", value);
        AddIfNotEmpty(parameters, "Description", description);
        AddIfNotEmpty(parameters, "Footprint", footprint);
        AddIfNotEmpty(parameters, "CurrentFootprint", footprint);
        AddIfNotEmpty(parameters, "LibraryRef", component.LibReference);
        AddIfNotEmpty(parameters, "DesignItemId", component.DesignItemId);

        return new AltiumBomRow(designator, value, footprint, description, parameters)
        {
            ProjectParameters = projectParameters,
        };
    }

    private static Dictionary<string, string> ReadComponentParameters(SchComponent component)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in component.Parameters.OfType<SchParameter>())
        {
            AddIfNotEmpty(parameters, parameter.Name, parameter.Value);
        }

        return parameters;
    }

    private static string ReadDesignator(SchComponent component, IReadOnlyDictionary<string, string> parameters)
    {
        var designatorParameter = component.Parameters
            .OfType<SchParameter>()
            .FirstOrDefault(p => string.Equals(p.Name, "Designator", StringComparison.OrdinalIgnoreCase));

        return FirstNotEmpty(
            designatorParameter?.PhysicalDesignator,
            designatorParameter?.Value,
            Lookup(parameters, "Designator"));
    }

    private static string ReadFootprint(SchComponent component)
    {
        var implementation = component.Implementations
            .OfType<SchImplementation>()
            .OrderByDescending(i => i.IsCurrent)
            .FirstOrDefault(i =>
                string.Equals(i.ModelType, "PCBLIB", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.ModelType, "PCB", StringComparison.OrdinalIgnoreCase));

        return implementation?.ModelName ?? "";
    }

    private static Dictionary<string, string> ReadProjectParameters(AltiumProject project)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in project.Parameters)
        {
            AddIfNotEmpty(parameters, parameter.Name, parameter.Value);
        }

        return parameters;
    }

    private static bool ProjectReferencesPcb(AltiumProject project, string projectPath, string pcbDocPath) =>
        project.PcbDocuments
            .Select(d => ResolveProjectDocumentPath(projectPath, d.DocumentPath))
            .Any(path => string.Equals(path, pcbDocPath, StringComparison.OrdinalIgnoreCase));

    private static string? ResolveProjectDocumentPath(string projectPath, string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return null;
        }

        var normalizedDocumentPath = documentPath.Replace('\\', '/').Trim();
        if (normalizedDocumentPath.Length == 0 || normalizedDocumentPath.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        var segments = new List<string>();
        foreach (var segment in RepositoryPath.GetParent(projectPath).Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            segments.Add(segment);
        }

        var documentSegments = normalizedDocumentPath.StartsWith('/')
            ? normalizedDocumentPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            : normalizedDocumentPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (normalizedDocumentPath.StartsWith('/'))
        {
            segments.Clear();
        }

        foreach (var rawSegment in documentSegments)
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return RepositoryPath.Normalize("/" + string.Join('/', segments));
    }

    private static string ChangeExtension(string path, string extension)
    {
        var parent = RepositoryPath.GetParent(path);
        var fileName = Path.GetFileNameWithoutExtension(path) + extension;
        return parent == "/" ? "/" + fileName : parent + "/" + fileName;
    }

    private async Task EnsureServeableFileAsync(
        string repoLocalPath,
        string path,
        long revision,
        long maxFileBytes,
        CancellationToken cancellationToken)
    {
        var size = await _svnlook.GetFileSizeAsync(repoLocalPath, path, revision, cancellationToken);
        if (size > maxFileBytes)
        {
            throw new InvalidOperationException($"Altium project file is too large: {path}");
        }
    }

    private static string? Lookup(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    private static void AddIfNotEmpty(Dictionary<string, string> fields, string? key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            fields.TryAdd(key, value);
        }
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}

public sealed record AltiumProjectBom(
    string ProjectPath,
    IReadOnlyDictionary<string, AltiumBomRow> Rows);

public sealed record AltiumProjectBomRenderSource(
    string ProjectPath,
    string PcbDocPath,
    byte[] PcbDocBytes,
    IReadOnlyDictionary<string, AltiumBomRow> Rows);
