using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "admin.users")]
public sealed class UsersImportModel : PageModel
{
    private const long MaxImportBytes = 2 * 1024 * 1024;

    private readonly UserService _users;

    public UsersImportModel(UserService users)
    {
        _users = users;
    }

    [BindProperty]
    public ImportInput Input { get; set; } = new();

    public string? Error { get; private set; }

    public HtpasswdImportResult? Result { get; private set; }

    public string ImportedRepositoryGrant =>
        User?.IsInRole(PortalUserRoleExtensions.RepoWriteClaim) ?? false
            ? PortalUserRoleExtensions.RepoWriteClaim
            : User?.IsInRole(PortalUserRoleExtensions.RepoReadClaim) ?? false
                ? PortalUserRoleExtensions.RepoReadClaim
                : "no inherited repository access";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var contentResult = await ReadImportContentAsync(cancellationToken);
        if (!contentResult.Success)
        {
            Error = contentResult.Error ?? "Failed to read import data.";
            return Page();
        }

        var result = await _users.ImportHtpasswdUsersAsync(actorId, contentResult.Value!, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Import failed.";
            return Page();
        }

        Result = result.Value;
        Input = new();
        return Page();
    }

    private async Task<ReadContentResult> ReadImportContentAsync(CancellationToken cancellationToken)
    {
        if (Input.File is { Length: > 0 })
        {
            if (Input.File.Length > MaxImportBytes)
            {
                return ReadContentResult.Fail("Import file is too large. Use at most 2 MB.");
            }

            using var reader = new StreamReader(Input.File.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return ReadContentResult.Ok(await reader.ReadToEndAsync(cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(Input.Text))
        {
            if (Encoding.UTF8.GetByteCount(Input.Text) > MaxImportBytes)
            {
                return ReadContentResult.Fail("Import text is too large. Use at most 2 MB.");
            }

            return ReadContentResult.Ok(Input.Text);
        }

        return ReadContentResult.Fail("Choose an htpasswd file or paste its contents.");
    }

    public sealed class ImportInput
    {
        [Display(Name = "htpasswd file")]
        public IFormFile? File { get; set; }

        [Display(Name = "Paste htpasswd contents")]
        public string? Text { get; set; }
    }

    private sealed record ReadContentResult(bool Success, string? Value = null, string? Error = null)
    {
        public static ReadContentResult Ok(string value) => new(true, value);
        public static ReadContentResult Fail(string error) => new(false, Error: error);
    }
}
