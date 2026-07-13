namespace SvnHub.Web.Support;

public sealed record RepositoryContextSidebarModel(
    string RepositoryName,
    IReadOnlyList<string> Labels,
    int IncomingRepositoryCount = 0,
    int IncomingReferenceCount = 0)
{
    public bool HasContent => Labels.Count > 0 || IncomingReferenceCount > 0;
}
