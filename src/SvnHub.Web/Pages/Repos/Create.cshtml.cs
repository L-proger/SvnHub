using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize(Roles = "AdminRepo")]
public sealed class CreateModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly SettingsService _settings;

    public CreateModel(RepositoryService repos, SettingsService settings)
    {
        _repos = repos;
        _settings = settings;
    }

    [BindProperty]
    public CreateRepoInput Input { get; set; } = new();

    public string? Error { get; private set; }
    public AccessLevel ServerDefaultAuthenticatedAccess { get; private set; } = AccessLevel.Write;

    public void OnGet()
    {
        ServerDefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ServerDefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        if (!TryParseDefaultAccess(Input.DefaultAuthenticatedAccess, out var repoDefault))
        {
            Error = "Invalid default access value.";
            return Page();
        }

        var result = await _repos.CreateAsync(
            actorId,
            Input.Name,
            repoDefault,
            Input.InitializeStandardLayout,
            cancellationToken);
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

        [Required]
        [Display(Name = "Default access for authenticated users")]
        public string DefaultAuthenticatedAccess { get; set; } = "Inherit";
    }

    private static bool TryParseDefaultAccess(string? value, out AccessLevel? result)
    {
        if (string.Equals(value, "Inherit", StringComparison.OrdinalIgnoreCase))
        {
            result = null;
            return true;
        }

        if (string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            result = AccessLevel.None;
            return true;
        }

        if (string.Equals(value, "Read", StringComparison.OrdinalIgnoreCase))
        {
            result = AccessLevel.Read;
            return true;
        }

        if (string.Equals(value, "Write", StringComparison.OrdinalIgnoreCase))
        {
            result = AccessLevel.Write;
            return true;
        }

        result = null;
        return false;
    }
}
