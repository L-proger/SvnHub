using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminRepo")]
public sealed class PermissionsModel : PageModel
{
    private readonly PermissionService _permissions;
    private readonly RepositoryService _repos;
    private readonly UserService _users;
    private readonly GroupService _groups;

    public PermissionsModel(
        PermissionService permissions,
        RepositoryService repos,
        UserService users,
        GroupService groups)
    {
        _permissions = permissions;
        _repos = repos;
        _users = users;
        _groups = groups;
    }

    [BindProperty]
    public AddRuleInput Input { get; set; } = new();

    public IReadOnlyList<PermissionRow> Rules { get; private set; } = [];
    public IReadOnlyList<Repository> RepositoryOptions { get; private set; } = [];
    public IReadOnlyList<PortalUser> UserOptions { get; private set; } = [];
    public IReadOnlyList<Group> GroupOptions { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet()
    {
        Load();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        Load();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var subjectType = Input.SubjectType switch
        {
            "Group" => SubjectType.Group,
            _ => SubjectType.User,
        };

        var subjectId = subjectType switch
        {
            SubjectType.Group => Input.GroupId,
            _ => Input.UserId,
        };

        if (subjectId is null || subjectId.Value == Guid.Empty)
        {
            Error = "Select a valid subject.";
            return Page();
        }

        var access = Input.Access switch
        {
            "Write" => AccessLevel.Write,
            "None" => AccessLevel.None,
            _ => AccessLevel.Read,
        };

        var result = await _permissions.AddRuleAsync(
            actorId,
            Input.RepositoryId,
            Input.Path,
            subjectType,
            subjectId.Value,
            access,
            cancellationToken);

        if (!result.Success)
        {
            Error = result.Error ?? "Failed to add rule.";
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        Load();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _permissions.DeleteRuleAsync(actorId, ruleId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to delete rule.";
            return Page();
        }

        return RedirectToPage();
    }

    private void Load()
    {
        RepositoryOptions = _repos.List();
        UserOptions = _users.ListUsers();
        GroupOptions = _groups.ListGroups();

        var repoNames = RepositoryOptions.ToDictionary(r => r.Id, r => r.Name);
        var userNames = UserOptions.ToDictionary(u => u.Id, u => u.UserName);
        var groupNames = GroupOptions.ToDictionary(g => g.Id, g => g.Name);

        Rules = _permissions.ListRules()
            .Select(r => new PermissionRow(
                r,
                RepositoryName: repoNames.GetValueOrDefault(r.RepositoryId, r.RepositoryId.ToString("D")),
                SubjectDisplay: r.SubjectType switch
                {
                    SubjectType.User => userNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    SubjectType.Group => "@" + groupNames.GetValueOrDefault(r.SubjectId, r.SubjectId.ToString("D")),
                    _ => r.SubjectId.ToString("D")
                }))
            .ToArray();
    }

    public sealed record PermissionRow(PermissionRule Rule, string RepositoryName, string SubjectDisplay)
    {
        public Guid Id => Rule.Id;
        public string Path => Rule.Path;
        public AccessLevel Access => Rule.Access;
        public DateTimeOffset CreatedAt => Rule.CreatedAt;
    }

    public sealed class AddRuleInput
    {
        [Required]
        [Display(Name = "Repository")]
        public Guid RepositoryId { get; set; }

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
}
