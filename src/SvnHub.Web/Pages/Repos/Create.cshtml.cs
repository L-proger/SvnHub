using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;

namespace SvnHub.Web.Pages.Repos;

[Authorize(Roles = "AdminRepo")]
public sealed class CreateModel : PageModel
{
    private readonly RepositoryService _repos;

    public CreateModel(RepositoryService repos)
    {
        _repos = repos;
    }

    [BindProperty]
    public CreateRepoInput Input { get; set; } = new();

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _repos.CreateAsync(actorId, Input.Name, Input.InitializeStandardLayout, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Failed to create repository.";
            return Page();
        }

        return RedirectToPage("/Repos/Tree", new { repoName = result.Value.Name });
    }

    public sealed class CreateRepoInput
    {
        [Required]
        [Display(Name = "Repository name")]
        public string Name { get; set; } = "";

        [Display(Name = "Create standard layout (trunk/branches/tags)")]
        public bool InitializeStandardLayout { get; set; } = true;
    }
}
