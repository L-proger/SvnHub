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
    private readonly PermissionService _permissions;
    private readonly UserService _users;
    private readonly GroupService _groups;
    private readonly SettingsService _settings;

    public SettingsModel(
        RepositoryService repos,
        PermissionService permissions,
        UserService users,
        GroupService groups,
        SettingsService settings)
    {
        _repos = repos;
        _permissions = permissions;
        _users = users;
        _groups = groups;
        _settings = settings;
    }

    public string RepoName { get; private set; } = "";
    public Guid RepoId { get; private set; }
    public AccessLevel ServerDefaultAuthenticatedAccess { get; private set; } = AccessLevel.Write;
    public AccessLevel? RepoAuthenticatedDefaultAccess { get; private set; }

    public AccessLevel EffectiveAuthenticatedDefaultAccess =>
        RepoAuthenticatedDefaultAccess ?? ServerDefaultAuthenticatedAccess;

    [BindProperty]
    public RenameInputModel RenameInput { get; set; } = new();

    [BindProperty]
    public DeleteInputModel DeleteInput { get; set; } = new();

    [BindProperty]
    public DefaultAccessInputModel DefaultAccessInput { get; set; } = new();

    [BindProperty]
    public AddAccessRuleInputModel AccessRuleInput { get; set; } = new();

    public IReadOnlyList<PermissionRow> AccessRules { get; private set; } = [];
    public IReadOnlyList<PortalUser> UserOptions { get; private set; } = [];
    public IReadOnlyList<Group> GroupOptions { get; private set; } = [];

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public IActionResult OnGet(string repoName)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        Load(repo);
        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        Load(repo);

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

        Load(repo);

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

    public async Task<IActionResult> OnPostDefaultAccessAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        Load(repo);

        // Validate only default access input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(DefaultAccessInput, nameof(DefaultAccessInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        if (!TryParseDefaultAccess(DefaultAccessInput.DefaultAuthenticatedAccess, out var parsed))
        {
            Error = "Invalid default access value.";
            return Page();
        }

        var result = await _repos.SetAuthenticatedDefaultAccessAsync(actorId, repo.Id, parsed, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Failed to update default access.";
            return Page();
        }

        // Keep other form defaults populated.
        Load(result.Value);
        Success = "Repository default access updated.";
        return Page();
    }

    public async Task<IActionResult> OnPostAddAccessRuleAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        Load(repo);

        // Validate only access-rule input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(AccessRuleInput, nameof(AccessRuleInput)))
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var subjectType = AccessRuleInput.SubjectType switch
        {
            "Group" => SubjectType.Group,
            _ => SubjectType.User,
        };

        var subjectId = subjectType switch
        {
            SubjectType.Group => AccessRuleInput.GroupId,
            _ => AccessRuleInput.UserId,
        };

        if (subjectId is null || subjectId.Value == Guid.Empty)
        {
            Error = "Select a valid subject.";
            return Page();
        }

        var access = AccessRuleInput.Access switch
        {
            "Write" => AccessLevel.Write,
            _ => AccessLevel.Read,
        };

        var result = await _permissions.AddRuleAsync(
            actorId,
            repo.Id,
            AccessRuleInput.Path,
            subjectType,
            subjectId.Value,
            access,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to add rule.";
            return Page();
        }

        return RedirectToPage(new { repoName = repo.Name });
    }

    public async Task<IActionResult> OnPostDeleteAccessRuleAsync(string repoName, Guid ruleId, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        Load(repo);

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var rule = AccessRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            Error = "Rule not found.";
            return Page();
        }

        var result = await _permissions.DeleteRuleAsync(actorId, ruleId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to delete rule.";
            return Page();
        }

        return RedirectToPage(new { repoName = repo.Name });
    }

    private void Load(Repository repo)
    {
        RepoName = repo.Name;
        RepoId = repo.Id;
        ServerDefaultAuthenticatedAccess = _settings.GetEffectiveDefaultAuthenticatedAccess();
        RepoAuthenticatedDefaultAccess = repo.AuthenticatedDefaultAccess;

        RenameInput.NewName = repo.Name;
        DefaultAccessInput.DefaultAuthenticatedAccess = FormatDefaultAccess(repo.AuthenticatedDefaultAccess);

        UserOptions = _users.ListUsers();
        GroupOptions = _groups.ListGroups();

        var userNames = UserOptions.ToDictionary(u => u.Id, u => u.UserName);
        var groupNames = GroupOptions.ToDictionary(g => g.Id, g => g.Name);

        AccessRules = _permissions.ListRules()
            .Where(r => r.RepositoryId == repo.Id)
            .Select(r => new PermissionRow(
                r,
                SubjectDisplay: r.SubjectType switch
                {
                    SubjectType.User => userNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    SubjectType.Group => "@" + groupNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    _ => r.SubjectId.ToString("D")
                }))
            .ToArray();
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

    public sealed class DefaultAccessInputModel
    {
        [Required]
        [Display(Name = "Default access for authenticated users")]
        public string DefaultAuthenticatedAccess { get; set; } = "Inherit";
    }

    public sealed record PermissionRow(PermissionRule Rule, string SubjectDisplay)
    {
        public Guid Id => Rule.Id;
        public string Path => Rule.Path;
        public AccessLevel Access => Rule.Access;
        public DateTimeOffset CreatedAt => Rule.CreatedAt;
    }

    public sealed class AddAccessRuleInputModel
    {
        [Display(Name = "Path")]
        public string Path { get; set; } = "/";

        [Required]
        [Display(Name = "Subject type")]
        public string SubjectType { get; set; } = "User";

        public Guid? UserId { get; set; }

        public Guid? GroupId { get; set; }

        [Required]
        public string Access { get; set; } = "Read";
    }

    private static string FormatDefaultAccess(AccessLevel? value) =>
        value switch
        {
            null => "Inherit",
            AccessLevel.None => "None",
            AccessLevel.Read => "Read",
            AccessLevel.Write => "Write",
            _ => "Inherit",
        };

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
