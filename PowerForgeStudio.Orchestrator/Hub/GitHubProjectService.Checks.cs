using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class GitHubProjectService
{
    /// <summary>Loads check runs and latest commit status per context. This is not a merge-policy decision.</summary>
    public async Task<GitHubPage<GitHubCheck>> FetchChecksAsync(string slug, string headSha, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(headSha ?? "", @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"))
            throw new ArgumentException("A full commit SHA is required.", nameof(headSha));
        var path = $"{RepositoryPath(slug)}/commits/{headSha}";
        var checks = await FetchPageAsync(path + "/check-runs?filter=latest", item => new GitHubCheck(
            Text(item, "name") ?? "Unnamed check", Text(item, "conclusion") ?? Text(item, "status") ?? "unknown",
            "check run", Text(item, "html_url")), cancellationToken, arrayProperty: "check_runs").ConfigureAwait(false);
        var statuses = await FetchPageAsync(path + "/statuses", item => new GitHubCheck(
            Text(item, "context") ?? "Unnamed status", Text(item, "state") ?? "unknown", "commit status", Text(item, "target_url")), cancellationToken).ConfigureAwait(false);
        // GitHub returns statuses newest first; old failures must not override a newer status in the same context.
        return new(checks.Concat(statuses.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(group => group.First())).ToArray(),
            checks.HasMore || statuses.HasMore);
    }

    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
        ? property.GetString() : null;
}
