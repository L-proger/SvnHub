using System.Security.Claims;
using SvnHub.App.Storage;
using SvnHub.Domain;

namespace SvnHub.App.Services;

public sealed class AccessService
{
    private readonly IPortalStore _store;

    public AccessService(IPortalStore store)
    {
        _store = store;
    }

    public AccessLevel GetAccess(Guid userId, Guid repositoryId, string path)
    {
        var state = _store.Read();
        return RepositoryAccessEvaluator.GetAccess(state, userId, repositoryId, path);
    }

    public static Guid? GetUserIdFromClaimsPrincipal(ClaimsPrincipal principal)
    {
        var idStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(idStr, out var id) ? id : null;
    }
}
