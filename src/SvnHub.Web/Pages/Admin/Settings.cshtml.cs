using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public sealed class SettingsModel : PageModel
{
    private readonly SettingsService _settings;

    public SettingsModel(SettingsService settings)
    {
        _settings = settings;
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public void OnGet()
    {
        Input.RepositoriesRootPath = _settings.GetEffectiveRepositoriesRootPath();
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

        var result = await _settings.SetRepositoriesRootPathAsync(
            actorId,
            Input.RepositoriesRootPath,
            Input.CreateIfMissing,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to save settings.";
            return Page();
        }

        Success = "Saved.";
        return Page();
    }

    public sealed class SettingsInput
    {
        [Required]
        [Display(Name = "Repositories root path")]
        public string RepositoriesRootPath { get; set; } = "";

        [Display(Name = "Create directory if missing")]
        public bool CreateIfMissing { get; set; } = true;
    }
}

