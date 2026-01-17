using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize(Roles = "AdminRepo")]
public sealed class SettingsModel : PageModel
{
    private readonly RepositoryService _repos;

    public SettingsModel(RepositoryService repos)
    {
        _repos = repos;
    }

    public string RepoName { get; private set; } = "";
    public Guid RepoId { get; private set; }

    [BindProperty]
    public RenameInputModel RenameInput { get; set; } = new();

    [BindProperty]
    public DeleteInputModel DeleteInput { get; set; } = new();

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public IActionResult OnGet(string repoName)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        RepoName = repo.Name;
        RepoId = repo.Id;
        RenameInput.NewName = repo.Name;
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        RepoName = repo.Name;
        RepoId = repo.Id;

        // Validate only rename input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(RenameInput, nameof(RenameInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _repos.RenameAsync(actorId, repo.Id, RenameInput.NewName, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Rename failed.";
            return Page();
        }

        return RedirectToPage("/Repos/Tree", new { repoName = result.Value.Name });
    }

    public async Task<IActionResult> OnPostDeleteAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        RepoName = repo.Name;
        RepoId = repo.Id;

        // Validate only delete input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(DeleteInput, nameof(DeleteInput)))
        {
            return Page();
        }

        if (string.IsNullOrWhiteSpace(DeleteInput.ConfirmName)
            || !string.Equals(DeleteInput.ConfirmName.Trim(), repo.Name, StringComparison.Ordinal))
        {
            ModelState.AddModelError($"{nameof(DeleteInput)}.{nameof(DeleteInputModel.ConfirmName)}", "Confirmation name does not match.");
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _repos.DeleteAsync(actorId, repo.Id, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Delete failed.";
            return Page();
        }

        return RedirectToPage("/Repos/Index");
    }

    public sealed class RenameInputModel
    {
        [Required]
        [Display(Name = "New name")]
        public string NewName { get; set; } = "";
    }

    public sealed class DeleteInputModel
    {
        [Required]
        [Display(Name = "Confirm repository name")]
        public string ConfirmName { get; set; } = "";
    }
}
