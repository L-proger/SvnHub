namespace SvnHub.Domain;

public sealed record PortalState(
    List<Repository> Repositories,
    List<PortalUser> Users,
    List<Group> Groups,
    List<GroupMember> GroupMembers,
    List<PermissionRule> PermissionRules,
    List<AuditEvent> AuditEvents
)
{
    public PortalSettings Settings { get; init; } = new();

    public static PortalState Empty() =>
        new(
            Repositories: [],
            Users: [],
            Groups: [],
            GroupMembers: [],
            PermissionRules: [],
            AuditEvents: []
        );

    public PortalState Snapshot() =>
        this with
        {
            Repositories = [..Repositories],
            Users = [..Users],
            Groups = [..Groups],
            GroupMembers = [..GroupMembers],
            PermissionRules = [..PermissionRules],
            AuditEvents = [..AuditEvents],
            Settings = Settings,
        };
}
