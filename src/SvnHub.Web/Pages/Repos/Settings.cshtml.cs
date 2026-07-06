using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.App.Support;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class SettingsModel : PageModel
{
    private readonly RepositoryService _repos;
    private readonly RepositoryManagementService _management;
    private readonly PermissionService _permissions;
    private readonly UserService _users;
    private readonly GroupService _groups;

    public SettingsModel(
        RepositoryService repos,
        RepositoryManagementService management,
        PermissionService permissions,
        UserService users,
        GroupService groups)
    {
        _repos = repos;
        _management = management;
        _permissions = permissions;
        _users = users;
        _groups = groups;
    }

    public string RepoName { get; private set; } = "";
    public Guid RepoId { get; private set; }
    public bool IncludeInheritedContentGrants { get; private set; } = true;
    public bool IncludeInheritedManagementGrants { get; private set; } = true;

    [BindProperty]
    public RenameInputModel RenameInput { get; set; } = new();

    [BindProperty]
    public DeleteInputModel DeleteInput { get; set; } = new();

    [BindProperty]
    public LabelsInputModel LabelsInput { get; set; } = new();

    [BindProperty]
    public AddAccessRuleInputModel AccessRuleInput { get; set; } = new();

    [BindProperty]
    public AddManagementGrantInputModel ManagementGrantInput { get; set; } = new();

    [BindProperty]
    public InheritedGrantsInputModel InheritedGrantsInput { get; set; } = new();

    public IReadOnlyList<PermissionRow> AccessRules { get; private set; } = [];
    public IReadOnlyList<ManagementGrantRow> ManagementGrants { get; private set; } = [];
    public IReadOnlyList<PortalUser> UserOptions { get; private set; } = [];
    public IReadOnlyList<Group> GroupOptions { get; private set; } = [];
    public IReadOnlyList<string> LabelSuggestions { get; private set; } = [];
    public bool CanAdministerRepository { get; private set; }
    public bool CanAdminRepository { get; private set; }
    public bool DisablingInheritedManagementGrantsRemovesYourAdminAccess { get; private set; }

    public string? Error { get; private set; }
    public string? Success { get; private set; }

    public IActionResult OnGet(string repoName)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);
        if (!CanAdministerRepository)
        {
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostRenameAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId, resetRenameInput: false);

        // Validate only rename input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(RenameInput, nameof(RenameInput)))
        {
            return Page();
        }

        var result = await _repos.RenameAsync(actorId, repo.Id, RenameInput.NewName, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Rename failed.";
            return Page();
        }

        return RedirectToPage("/Repos/Tree", new { repoName = result.Value.Name });
    }

    public async Task<IActionResult> OnPostLabelsAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId, resetLabelsInput: false);

        ModelState.Clear();
        if (!TryValidateModel(LabelsInput, nameof(LabelsInput)))
        {
            return Page();
        }

        var result = await _repos.SetLabelsAsync(actorId, repo.Id, LabelsInput.Labels, cancellationToken);
        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Failed to update labels.";
            return Page();
        }

        Load(result.Value, actorId);
        Success = "Repository labels updated.";
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

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

        var result = await _repos.DeleteAsync(actorId, repo.Id, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Delete failed.";
            return Page();
        }

        return RedirectToPage("/Repos/Index");
    }

    public async Task<IActionResult> OnPostAddAccessRuleAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

        // Validate only access-rule input for this handler.
        ModelState.Clear();
        if (!TryValidateModel(AccessRuleInput, nameof(AccessRuleInput)))
        {
            return Page();
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
            "None" => AccessLevel.None,
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

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

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

    public async Task<IActionResult> OnPostMoveAccessRuleAsync(
        string repoName,
        Guid ruleId,
        string direction,
        CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

        var rule = AccessRules.FirstOrDefault(r => r.Id == ruleId);
        if (rule is null)
        {
            Error = "Rule not found.";
            return Page();
        }

        var moveUp = string.Equals(direction, "up", StringComparison.OrdinalIgnoreCase);
        var result = await _permissions.MoveRuleAsync(actorId, ruleId, moveUp, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to move rule.";
            return Page();
        }

        return RedirectToPage(new { repoName = repo.Name });
    }

    public async Task<IActionResult> OnPostAddManagementGrantAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

        ModelState.Clear();
        if (!TryValidateModel(ManagementGrantInput, nameof(ManagementGrantInput)))
        {
            return Page();
        }

        var subjectType = ManagementGrantInput.SubjectType switch
        {
            "Group" => SubjectType.Group,
            _ => SubjectType.User,
        };

        var subjectId = subjectType switch
        {
            SubjectType.Group => ManagementGrantInput.GroupId,
            _ => ManagementGrantInput.UserId,
        };

        if (subjectId is null || subjectId.Value == Guid.Empty)
        {
            Error = "Select a valid subject.";
            return Page();
        }

        var result = await _management.AddGrantAsync(
            actorId,
            repo.Id,
            subjectType,
            subjectId.Value,
            RepositoryManagementRole.Admin,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to add repository administrator.";
            return Page();
        }

        return RedirectToPage(new { repoName = repo.Name });
    }

    public async Task<IActionResult> OnPostInheritedGrantsAsync(string repoName, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId, resetInheritedGrantsInput: false);

        ModelState.Clear();
        if (!TryValidateModel(InheritedGrantsInput, nameof(InheritedGrantsInput)))
        {
            return Page();
        }

        var result = await _repos.SetInheritedRepositoryGrantsAsync(
            actorId,
            repo.Id,
            InheritedGrantsInput.IncludeInheritedContentGrants,
            InheritedGrantsInput.IncludeInheritedManagementGrants,
            cancellationToken);

        if (!result.Success || result.Value is null)
        {
            Error = result.Error ?? "Failed to update inherited repository grants.";
            return Page();
        }

        Load(result.Value, actorId);
        Success = "Inherited repository grants updated.";
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteManagementGrantAsync(string repoName, Guid grantId, CancellationToken cancellationToken)
    {
        var repo = _repos.FindByName(repoName);
        if (repo is null || repo.IsArchived)
        {
            return NotFound();
        }

        if (!TryGetActorId(out var actorId))
        {
            return Forbid();
        }

        Load(repo, actorId);

        var grant = ManagementGrants.FirstOrDefault(g => g.Id == grantId);
        if (grant is null)
        {
            Error = "Repository management grant not found.";
            return Page();
        }

        var result = await _management.DeleteGrantAsync(actorId, grantId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to delete repository management grant.";
            return Page();
        }

        return RedirectToPage(new { repoName = repo.Name });
    }

    private void Load(
        Repository repo,
        Guid actorId,
        bool resetRenameInput = true,
        bool resetLabelsInput = true,
        bool resetInheritedGrantsInput = true)
    {
        RepoName = repo.Name;
        RepoId = repo.Id;
        IncludeInheritedContentGrants = repo.IncludeInheritedContentGrants;
        IncludeInheritedManagementGrants = repo.IncludeInheritedManagementGrants;
        CanAdministerRepository = _management.CanAdminRepository(actorId, repo.Id);
        CanAdminRepository = _management.CanAdminRepository(actorId, repo.Id);
        DisablingInheritedManagementGrantsRemovesYourAdminAccess =
            _management.WouldDisablingInheritedManagementGrantsRemoveAdmin(actorId, repo.Id);

        if (resetRenameInput)
        {
            RenameInput.NewName = repo.Name;
        }

        if (resetLabelsInput)
        {
            LabelsInput.Labels = string.Join(", ", RepositoryLabels.Normalize(repo.Labels));
        }

        if (resetInheritedGrantsInput)
        {
            InheritedGrantsInput.IncludeInheritedContentGrants = repo.IncludeInheritedContentGrants;
            InheritedGrantsInput.IncludeInheritedManagementGrants = repo.IncludeInheritedManagementGrants;
        }

        LabelSuggestions = RepositoryLabels.Collect(_repos.List());

        UserOptions = _users.ListUsers();
        GroupOptions = _groups.ListGroups();

        var userNames = UserOptions.ToDictionary(u => u.Id, u => u.UserName);
        var groupNames = GroupOptions.ToDictionary(g => g.Id, g => g.Name);

        var accessRules = _permissions.ListRules()
            .Where(r => r.RepositoryId == repo.Id)
            .ToArray();

        AccessRules = accessRules
            .Select((r, index) => new PermissionRow(
                r,
                Order: index + 1,
                SubjectDisplay: r.SubjectType switch
                {
                    SubjectType.User => userNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    SubjectType.Group => "@" + groupNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    _ => r.SubjectId.ToString("D")
                },
                CanMoveUp: index > 0,
                CanMoveDown: index < accessRules.Length - 1))
            .ToArray();

        ManagementGrants = _management.ListGrants()
            .Where(g => g.RepositoryId == repo.Id)
            .Select(g => new ManagementGrantRow(
                g,
                SubjectDisplay: g.SubjectType switch
                {
                    SubjectType.User => userNames.GetValueOrDefault(g.SubjectId, g.SubjectId.ToString("D")),
                    SubjectType.Group => "@" + groupNames.GetValueOrDefault(g.SubjectId, g.SubjectId.ToString("D")),
                    _ => g.SubjectId.ToString("D")
                },
                DeletingRemovesYourAdminAccess: _management.WouldDeletingGrantRemoveAdmin(actorId, g.Id)))
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

    public sealed class LabelsInputModel
    {
        [Display(Name = "Labels")]
        public string Labels { get; set; } = "";
    }

    public sealed record PermissionRow(
        PermissionRule Rule,
        int Order,
        string SubjectDisplay,
        bool CanMoveUp,
        bool CanMoveDown)
    {
        public Guid Id => Rule.Id;
        public string Path => Rule.Path;
        public AccessLevel Access => Rule.Access;
        public DateTimeOffset CreatedAt => Rule.CreatedAt;
    }

    public sealed record ManagementGrantRow(
        RepositoryManagementGrant Grant,
        string SubjectDisplay,
        bool DeletingRemovesYourAdminAccess)
    {
        public Guid Id => Grant.Id;
        public RepositoryManagementRole Role => Grant.Role;
        public string RoleDisplay => Role switch
        {
            RepositoryManagementRole.Admin => "Repository Admin",
            _ => Role.ToString(),
        };
        public DateTimeOffset CreatedAt => Grant.CreatedAt;
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

    public sealed class AddManagementGrantInputModel
    {
        [Required]
        [Display(Name = "Subject type")]
        public string SubjectType { get; set; } = "User";

        public Guid? UserId { get; set; }

        public Guid? GroupId { get; set; }
    }

    public sealed class InheritedGrantsInputModel
    {
        [Display(Name = "Inherit global SVN content grants")]
        public bool IncludeInheritedContentGrants { get; set; } = true;

        [Display(Name = "Inherit global repo.admin grants")]
        public bool IncludeInheritedManagementGrants { get; set; } = true;

    }

    private bool TryGetActorId(out Guid actorId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out actorId);

}
