using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminSystem")]
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
        Input.SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();
        Input.MaxUploadMegabytes = (int)Math.Clamp(_settings.GetEffectiveMaxUploadBytes() / (1024 * 1024), 1, int.MaxValue);
        Input.DefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();
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
            Input.SvnBaseUrl,
            Input.DefaultAuthenticatedAccess,
            (long)Math.Max(1, Input.MaxUploadMegabytes) * 1024 * 1024,
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

        [Display(Name = "SVN base URL")]
        public string SvnBaseUrl { get; set; } = "";

        [Display(Name = "Default access for authenticated users")]
        public AccessLevel DefaultAuthenticatedAccess { get; set; } = AccessLevel.Write;

        [Range(1, 2048)]
        [Display(Name = "Max upload size (MB)")]
        public int MaxUploadMegabytes { get; set; } = 100;

        [Display(Name = "Create folder if missing")]
        public bool CreateIfMissing { get; set; } = true;
    }
}
