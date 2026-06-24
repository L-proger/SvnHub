using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminSystem")]
public sealed class SettingsModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly BrandingService _branding;

    public SettingsModel(SettingsService settings, BrandingService branding)
    {
        _settings = settings;
        _branding = branding;
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? FaviconFile { get; set; }

    public bool HasCustomFavicon { get; private set; }
    public string FaviconHref { get; private set; } = "~/favicon.svg";
    public string FaviconVersion { get; private set; } = "default";
    public int MaxFaviconKilobytes => (int)(BrandingService.MaxFaviconBytes / 1024);

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public void OnGet()
    {
        LoadSettingsInput();
        LoadBrandingState();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            LoadBrandingState();
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
            Input.OrganizationName,
            Input.SvnBaseUrl,
            Input.DefaultAuthenticatedAccess,
            (long)Math.Max(1, Input.MaxUploadMegabytes) * 1024 * 1024,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to save settings.";
            LoadBrandingState();
            return Page();
        }

        Success = "Saved.";
        LoadBrandingState();
        return Page();
    }

    public async Task<IActionResult> OnPostUploadFaviconAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        LoadSettingsInput();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        if (FaviconFile is null || FaviconFile.Length <= 0)
        {
            Error = "Choose a PNG or ICO icon file.";
            LoadBrandingState();
            return Page();
        }

        await using var stream = FaviconFile.OpenReadStream();
        var result = await _branding.SetFaviconAsync(
            actorId,
            FaviconFile.FileName,
            stream,
            FaviconFile.Length,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to upload icon.";
            LoadBrandingState();
            return Page();
        }

        Success = "Icon updated.";
        LoadBrandingState();
        return Page();
    }

    public IActionResult OnPostResetFavicon()
    {
        ModelState.Clear();
        LoadSettingsInput();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = _branding.ResetFavicon(actorId);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to reset icon.";
            LoadBrandingState();
            return Page();
        }

        Success = "Icon reset.";
        LoadBrandingState();
        return Page();
    }

    private void LoadSettingsInput()
    {
        Input.OrganizationName = _settings.GetOrganizationName();
        Input.RepositoriesRootPath = _settings.GetEffectiveRepositoriesRootPath();
        Input.SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();
        Input.MaxUploadMegabytes = (int)Math.Clamp(_settings.GetEffectiveMaxUploadBytes() / (1024 * 1024), 1, int.MaxValue);
        Input.DefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();
    }

    private void LoadBrandingState()
    {
        var faviconLink = _branding.GetFaviconLink();
        HasCustomFavicon = _branding.GetCustomFavicon() is not null;
        FaviconHref = faviconLink.Href;
        FaviconVersion = _branding.GetFaviconVersion();
    }

    public sealed class SettingsInput
    {
        [StringLength(80)]
        [Display(Name = "Organization")]
        public string OrganizationName { get; set; } = "";

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
