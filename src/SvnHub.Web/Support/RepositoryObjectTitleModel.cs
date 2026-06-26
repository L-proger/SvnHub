namespace SvnHub.Web.Support;

public sealed record RepositoryObjectTitleModel(
    string Path,
    bool IsDirectory,
    IReadOnlyList<string> Metadata);
