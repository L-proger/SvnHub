namespace SvnHub.Web.Support;

public sealed record RepositoryObjectCardModel(
    RepositoryObjectTitleModel Title,
    IReadOnlyList<RepositoryObjectActionModel> Actions,
    string BodyPartialName,
    object BodyModel,
    bool WrapBodyInCardBody = true,
    string? CopyMessageElementId = null);
