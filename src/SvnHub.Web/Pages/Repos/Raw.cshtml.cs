using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.Web.Support;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class RawModel : PageModel
{
    private readonly RepositoryFileResponseService _files;

    public RawModel(RepositoryFileResponseService files)
    {
        _files = files;
    }

    public Task<IActionResult> OnGetAsync(string repoName, string? path, long? rev, CancellationToken cancellationToken) =>
        _files.ServeRawAsync(User, Response, repoName, path, rev, cancellationToken);
}
