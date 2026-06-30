namespace SvnHub.Web.Support;

public sealed record RepositoryRevisionNoticeModel(string RepositoryName, long Revision, long HeadRevision);
