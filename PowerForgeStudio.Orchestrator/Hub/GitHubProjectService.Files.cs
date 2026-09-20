using System.Text.Json;
using System.Text.RegularExpressions;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Hub;

public sealed partial class GitHubProjectService
{
    /// <summary>Rejects a changed head/base instead of showing patches from a different PR revision.</summary>
    public async Task<GitHubPullRequestFiles> FetchPullRequestFilesAsync(string slug, int number, string expectedHeadSha,
        CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(expectedHeadSha ?? "", @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z"))
            throw new ArgumentException("A full commit SHA is required.", nameof(expectedHeadSha));
        var path = $"{RepositoryPath(slug)}/pulls/{ValidateNumber(number)}";
        using var before = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
        var head = Revision(before.Document.RootElement, "head");
        var baseSha = Revision(before.Document.RootElement, "base");
        if (!string.Equals(head, expectedHeadSha, StringComparison.OrdinalIgnoreCase)) throw new GitHubRevisionChangedException();
        var files = await FetchPageAsync(path + "/files", ParseFile, cancellationToken).ConfigureAwait(false);
        using var after = await ReadJsonAsync(path, cancellationToken).ConfigureAwait(false);
        if (head != Revision(after.Document.RootElement, "head") || baseSha != Revision(after.Document.RootElement, "base"))
            throw new GitHubRevisionChangedException();
        var total = after.Document.RootElement.TryGetProperty("changed_files", out var changed) ? changed.GetInt32() : (int?)null;
        return new(head, baseSha, files with { HasMore = files.HasMore || total is null || total > files.Count });
    }

    private static string Revision(JsonElement root, string name)
    {
        var revision = root.TryGetProperty(name, out var item) ? Text(item, "sha") : null;
        return revision is not null && Regex.IsMatch(revision, @"\A(?:[a-fA-F0-9]{40}|[a-fA-F0-9]{64})\z") ? revision
            : throw new InvalidDataException("GitHub did not return a valid PR revision.");
    }

    private static GitHubPullRequestFile ParseFile(JsonElement item)
    {
        const int maximumPatchCharacters = 256 * 1024;
        var patch = Text(item, "patch");
        var truncated = patch?.Length > maximumPatchCharacters;
        if (truncated) patch = patch![..maximumPatchCharacters];
        return new(item.GetProperty("filename").GetString() ?? throw new InvalidDataException("Missing changed-file path."),
            Text(item, "previous_filename"), Text(item, "status") ?? "unknown",
            item.GetProperty("additions").GetInt32(), item.GetProperty("deletions").GetInt32(), patch, truncated);
    }
}

/// <summary>Indicates that the displayed PR revision must be refreshed before reviewing its files.</summary>
public sealed class GitHubRevisionChangedException() : Exception("The PR head or base changed while loading files. Reload the PR detail, then review files again.");
