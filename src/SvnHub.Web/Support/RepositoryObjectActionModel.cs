namespace SvnHub.Web.Support;

public sealed record RepositoryObjectActionModel(
    string Text,
    string? Href = null,
    string ButtonClass = "btn-outline-secondary",
    bool OpenInNewTab = false,
    string? CopyText = null,
    string? CopyFrom = null,
    string? CopyMessageTarget = null,
    string? FormAction = null,
    string? Confirm = null,
    IReadOnlyDictionary<string, string?>? HiddenFields = null,
    string? UploadOpenTarget = null,
    IReadOnlyList<RepositoryObjectActionModel>? Items = null)
{
    public bool IsDropdown => Items is { Count: > 0 };
    public bool IsForm => !string.IsNullOrWhiteSpace(FormAction);
    public bool IsCopy => !string.IsNullOrWhiteSpace(CopyText) || !string.IsNullOrWhiteSpace(CopyFrom);
    public bool IsUploadOpenButton => !string.IsNullOrWhiteSpace(UploadOpenTarget);
}
