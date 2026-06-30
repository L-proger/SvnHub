namespace SvnHub.Domain;

public sealed record PortalState(
    List<Repository> Repositories,
    List<PortalUser> Users,
    List<Group> Groups,
    List<GroupMember> GroupMembers,
    List<GroupGroupMember> GroupGroupMembers,
    List<PermissionRule> PermissionRules,
    List<RepositoryManagementGrant> RepositoryManagementGrants,
    List<ApiToken> ApiTokens,
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
            GroupGroupMembers: [],
            PermissionRules: [],
            RepositoryManagementGrants: [],
            ApiTokens: [],
            AuditEvents: []
        );

    public PortalState Snapshot() =>
        this with
        {
            Repositories = [..Repositories],
            Users = [..Users],
            Groups = [..Groups],
            GroupMembers = [..GroupMembers],
            GroupGroupMembers = [..GroupGroupMembers],
            PermissionRules = [..PermissionRules],
            RepositoryManagementGrants = [..RepositoryManagementGrants],
            ApiTokens = [..ApiTokens],
            AuditEvents = [..AuditEvents],
            Settings = Settings,
        };
}
