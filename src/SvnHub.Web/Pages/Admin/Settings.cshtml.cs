using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Indexing;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminSystem")]
public sealed class SettingsModel : PageModel
{
    private readonly SettingsService _settings;
    private readonly BrandingService _branding;
    private readonly RepositoryIndexService _index;
    private readonly IHostApplicationLifetime _applicationLifetime;

    public SettingsModel(
        SettingsService settings,
        BrandingService branding,
        RepositoryIndexService index,
        IHostApplicationLifetime applicationLifetime)
    {
        _settings = settings;
        _branding = branding;
        _index = index;
        _applicationLifetime = applicationLifetime;
    }

    [BindProperty]
    public SettingsInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? FaviconFile { get; set; }

    public bool HasCustomFavicon { get; private set; }
    public string FaviconHref { get; private set; } = "~/favicon.svg";
    public string FaviconVersion { get; private set; } = "default";
    public int MaxFaviconKilobytes => (int)(BrandingService.MaxFaviconBytes / 1024);
    public RepositoryIndexStatus? IndexStatus { get; private set; }
    public long IndexRemainingRevisions =>
        IndexStatus?.Store.Repositories
            .Where(r => !r.IsMissing)
            .Sum(GetRemainingRevisions) ?? 0;
    public int IndexPendingRepositoryCount =>
        IndexStatus?.Store.Repositories.Count(r => !r.IsMissing && GetRemainingRevisions(r) > 0) ?? 0;
    public IReadOnlyList<RepositoryIndexRepositoryState> IndexRepositoryRows =>
        IndexStatus?.Store.Repositories
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.LastError))
            .ThenByDescending(GetRemainingRevisions)
            .ThenBy(r => r.IsMissing)
            .ThenBy(r => r.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray() ?? [];
    public int IndexHiddenRepositoryCount =>
        Math.Max(0, (IndexStatus?.Store.Repositories.Count ?? 0) - IndexRepositoryRows.Count);

    [TempData]
    public string? Error { get; set; }

    [TempData]
    public string? Success { get; set; }

    public async Task OnGet(CancellationToken cancellationToken)
    {
        LoadSettingsInput();
        await LoadNonSettingsStateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadNonSettingsStateAsync(cancellationToken);
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
            Input.IndexingEnabled,
            Input.IndexingScanIntervalSeconds,
            Input.IndexingMaxRevisionsPerRepositoryPerScan,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to save settings.";
            await LoadNonSettingsStateAsync(cancellationToken);
            return Page();
        }

        Success = "Saved.";
        return RedirectToPage();
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
            await LoadNonSettingsStateAsync(cancellationToken);
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
            await LoadNonSettingsStateAsync(cancellationToken);
            return Page();
        }

        Success = "Icon updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetFavicon(CancellationToken cancellationToken)
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
            await LoadNonSettingsStateAsync(cancellationToken);
            return Page();
        }

        Success = "Icon reset.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostScanIndexNowAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        LoadSettingsInput();

        var result = _index.StartScanInBackground(force: true, _applicationLifetime.ApplicationStopping);
        if (result.Skipped)
        {
            Error = result.Message;
        }
        else if (!result.Started)
        {
            Error = result.Message;
        }
        else
        {
            Success = result.Message;
        }

        return RedirectToPage();
    }

    private void LoadSettingsInput()
    {
        Input.OrganizationName = _settings.GetOrganizationName();
        Input.RepositoriesRootPath = _settings.GetEffectiveRepositoriesRootPath();
        Input.SvnBaseUrl = _settings.GetEffectiveSvnBaseUrl();
        Input.MaxUploadMegabytes = (int)Math.Clamp(_settings.GetEffectiveMaxUploadBytes() / (1024 * 1024), 1, int.MaxValue);
        Input.DefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();

        var indexing = _settings.GetEffectiveIndexingSettings();
        Input.IndexingEnabled = indexing.Enabled;
        Input.IndexingScanIntervalSeconds = indexing.ScanIntervalSeconds;
        Input.IndexingMaxRevisionsPerRepositoryPerScan = indexing.MaxRevisionsPerRepositoryPerScan;
    }

    private async Task LoadNonSettingsStateAsync(CancellationToken cancellationToken)
    {
        var faviconLink = _branding.GetFaviconLink();
        HasCustomFavicon = _branding.GetCustomFavicon() is not null;
        FaviconHref = faviconLink.Href;
        FaviconVersion = _branding.GetFaviconVersion();
        IndexStatus = await _index.GetStatusAsync(cancellationToken);
    }

    public static string FormatDate(DateTimeOffset? value) =>
        value is null ? "-" : value.Value.ToLocalTime().ToString("g");

    public static long GetRemainingRevisions(RepositoryIndexRepositoryState state) =>
        Math.Max(0, state.YoungestRevision - state.IndexedRevision);

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

        [Display(Name = "Run background indexer automatically")]
        public bool IndexingEnabled { get; set; }

        [Range(
            SettingsService.MinIndexingScanIntervalSeconds,
            SettingsService.MaxIndexingScanIntervalSeconds)]
        [Display(Name = "Scan interval (seconds)")]
        public int IndexingScanIntervalSeconds { get; set; } = SettingsService.DefaultIndexingScanIntervalSeconds;

        [Range(
            SettingsService.MinIndexingMaxRevisionsPerRepositoryPerScan,
            SettingsService.MaxIndexingMaxRevisionsPerRepositoryPerScan)]
        [Display(Name = "Max revisions per repository per scan")]
        public int IndexingMaxRevisionsPerRepositoryPerScan { get; set; } =
            SettingsService.DefaultIndexingMaxRevisionsPerRepositoryPerScan;

        [Display(Name = "Create folder if missing")]
        public bool CreateIfMissing { get; set; } = true;
    }
}
