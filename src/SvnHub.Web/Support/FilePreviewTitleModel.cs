namespace SvnHub.Web.Support;

public sealed record FilePreviewTitleModel(
    string Path,
    string? FileSizeLabel = null,
    int? LineCount = null)
{
    public RepositoryObjectTitleModel ToRepositoryObjectTitle()
    {
        var metadata = new List<string>();
        if (!string.IsNullOrWhiteSpace(FileSizeLabel))
        {
            metadata.Add(FileSizeLabel);
        }

        if (LineCount is not null)
        {
            metadata.Add($"{LineCount.Value} {(LineCount.Value == 1 ? "line" : "lines")}");
        }

        return new RepositoryObjectTitleModel(Path, IsDirectory: false, metadata);
    }
}
