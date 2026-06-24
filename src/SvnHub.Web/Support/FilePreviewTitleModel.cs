namespace SvnHub.Web.Support;

public sealed record FilePreviewTitleModel(
    string Path,
    string? FileSizeLabel = null,
    int? LineCount = null);
