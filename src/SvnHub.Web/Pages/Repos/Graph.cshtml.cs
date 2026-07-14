using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SvnHub.App.Services;
using SvnHub.Domain;

namespace SvnHub.Web.Pages.Repos;

[Authorize]
public sealed class GraphModel : PageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RepositoryExternalReferenceService _references;
    private readonly UserService _users;

    public GraphModel(
        RepositoryExternalReferenceService references,
        UserService users)
    {
        _references = references;
        _users = users;
    }

    public string GraphJson { get; private set; } = "{}";
    public int RepositoryCount { get; private set; }
    public int ConnectedRepositoryCount { get; private set; }
    public int ConnectionCount { get; private set; }
    public int ReferenceCount { get; private set; }
    public TimeSpan QueryDuration { get; private set; }
    public string QueryDurationLabel =>
        QueryDuration.TotalMilliseconds < 0.1
            ? "<0.1 ms"
            : $"{QueryDuration.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture)} ms";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = AccessService.GetUserIdFromClaimsPrincipal(User);
        if (userId is null)
        {
            return Forbid();
        }

        var startedAt = Stopwatch.GetTimestamp();
        var snapshot = await _references.GetDependencyGraphAsync(userId.Value, cancellationToken);
        QueryDuration = Stopwatch.GetElapsedTime(startedAt);

        RepositoryCount = snapshot.Nodes.Count;
        ConnectedRepositoryCount = snapshot.Nodes.Count(node =>
            node.IncomingRepositoryCount > 0 ||
            node.OutgoingRepositoryCount > 0);
        ConnectionCount = snapshot.Edges.Count;
        ReferenceCount = snapshot.InterRepositoryReferenceCount + snapshot.SelfReferenceCount;
        var openTrunkWhenAvailable = _users.ListUsers()
            .FirstOrDefault(user => user.Id == userId.Value && user.IsActive)?
            .RepositoryOpenBehavior != PortalRepositoryOpenBehavior.RepositoryRoot;

        var data = new RepositoryGraphData(
            snapshot.Nodes.Select(node => new RepositoryGraphNodeData(
                node.RepositoryId.ToString("N"),
                node.Name,
                node.Labels,
                node.IncomingRepositoryCount,
                node.OutgoingRepositoryCount,
                node.IncomingReferenceCount,
                node.OutgoingReferenceCount,
                node.SelfReferenceCount,
                Url.Page("/Repos/Tree", new
                {
                    repoName = node.Name,
                    defaultPath = openTrunkWhenAvailable ? "true" : null,
                }) ?? $"/repos/{Uri.EscapeDataString(node.Name)}/tree",
                Url.Page("/Repos/ExternalReferences", new { repoName = node.Name }) ?? $"/repos/{Uri.EscapeDataString(node.Name)}/externals"))
                .ToArray(),
            snapshot.Edges.Select(edge => new RepositoryGraphEdgeData(
                edge.SourceRepositoryId.ToString("N"),
                edge.TargetRepositoryId.ToString("N"),
                edge.ReferenceCount,
                edge.PinnedReferenceCount,
                edge.UnpinnedReferenceCount))
                .ToArray());
        GraphJson = JsonSerializer.Serialize(data, JsonOptions);
        return Page();
    }

    private sealed record RepositoryGraphData(
        IReadOnlyList<RepositoryGraphNodeData> Nodes,
        IReadOnlyList<RepositoryGraphEdgeData> Edges);

    private sealed record RepositoryGraphNodeData(
        string Id,
        string Name,
        IReadOnlyList<string> Labels,
        int IncomingRepositoryCount,
        int OutgoingRepositoryCount,
        int IncomingReferenceCount,
        int OutgoingReferenceCount,
        int SelfReferenceCount,
        string RepositoryUrl,
        string ExternalReferencesUrl);

    private sealed record RepositoryGraphEdgeData(
        string Source,
        string Target,
        int ReferenceCount,
        int PinnedReferenceCount,
        int UnpinnedReferenceCount);
}
