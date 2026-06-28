namespace SvnHub.Web.Support;

public sealed record RepositoryContextSidebarModel(IReadOnlyList<string> Labels)
{
    public bool HasContent => Labels.Count > 0;
}
