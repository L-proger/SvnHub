namespace SvnHub.Web.Support;

public sealed record RepositoryBreadcrumbModel(string RepositoryName, string Path, long? Revision = null);
