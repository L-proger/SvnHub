using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Admin;

[Authorize(Roles = "AdminRepo")]
public sealed class GroupsModel : PageModel
{
    private readonly GroupService _groups;
    private readonly UserService _users;

    public GroupsModel(GroupService groups, UserService users)
    {
        _groups = groups;
        _users = users;
    }

    [BindProperty]
    public CreateGroupInputModel CreateGroupInput { get; set; } = new();

    [BindProperty]
    public AddMemberInputModel AddMemberInput { get; set; } = new();

    [BindProperty]
    public AddSubgroupInputModel AddSubgroupInput { get; set; } = new();

    public IReadOnlyList<GroupRow> Groups { get; private set; } = [];
    public IReadOnlyList<Group> GroupOptions { get; private set; } = [];
    public IReadOnlyList<PortalUser> UserOptions { get; private set; } = [];
    public string? Error { get; private set; }

    public void OnGet()
    {
        Load();
    }

    public async Task<IActionResult> OnPostCreateGroupAsync(CancellationToken cancellationToken)
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

        var result = await _groups.CreateGroupAsync(actorId, CreateGroupInput.Name, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to create group.";
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddMemberAsync(CancellationToken cancellationToken)
    {
        Load();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _groups.AddMemberAsync(actorId, AddMemberInput.GroupId, AddMemberInput.UserId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to add member.";
            return Page();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostAddSubgroupAsync(CancellationToken cancellationToken)
    {
        Load();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actorId))
        {
            return Forbid();
        }

        var result = await _groups.AddSubgroupAsync(actorId, AddSubgroupInput.GroupId, AddSubgroupInput.ChildGroupId, cancellationToken);
        if (!result.Success)
        {
            Error = result.Error ?? "Failed to add subgroup.";
            return Page();
        }

        return RedirectToPage();
    }

    private void Load()
    {
        Groups = _groups.ListGroupsWithDetails()
            .Select(x => new GroupRow(x.Group, x.Members, x.Subgroups))
            .ToArray();

        GroupOptions = _groups.ListGroups();
        UserOptions = _users.ListUsers();
    }

    public sealed record GroupRow(Group Group, string[] Members, string[] Subgroups)
    {
        public Guid Id => Group.Id;
        public string Name => Group.Name;
    }

    public sealed class CreateGroupInputModel
    {
        [Required]
        [Display(Name = "Group name")]
        public string Name { get; set; } = "";
    }

    public sealed class AddMemberInputModel
    {
        [Required]
        public Guid GroupId { get; set; }

        [Required]
        public Guid UserId { get; set; }
    }

    public sealed class AddSubgroupInputModel
    {
        [Required]
        public Guid GroupId { get; set; }

        [Required]
        public Guid ChildGroupId { get; set; }
    }
}
