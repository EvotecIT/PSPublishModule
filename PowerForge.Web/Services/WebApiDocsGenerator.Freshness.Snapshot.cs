using System.Collections.Concurrent;
using PowerForge;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // Immutable Git history is reused across catalogs, bounded to eight repository/revision snapshots.
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyDictionary<string, (string Commit, DateTimeOffset Modified)>>> GitFreshnessSnapshots = new();

    private static IReadOnlyDictionary<string, (string Commit, DateTimeOffset Modified)> GetGitFreshnessSnapshot(string root, GitClient git)
    {
        var identity = git.RunRawAsync(root, new[] { "rev-parse", "HEAD", "--git-common-dir" }).GetAwaiter().GetResult();
        var lines = identity.StdOut.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (!identity.Succeeded || lines.Length != 2) return new Dictionary<string, (string, DateTimeOffset)>();
        var common = Path.GetFullPath(Path.Combine(root, lines[1]));
        var shallow = Path.Combine(common, "shallow");
        var key = root + "\0" + lines[0] + "\0" + (File.Exists(shallow) ? File.GetLastWriteTimeUtc(shallow).Ticks : 0);
        if (GitFreshnessSnapshots.Count >= 8 && !GitFreshnessSnapshots.ContainsKey(key)) GitFreshnessSnapshots.Clear();
        var snapshot = GitFreshnessSnapshots.GetOrAdd(key, _ => new Lazy<IReadOnlyDictionary<string, (string Commit, DateTimeOffset Modified)>>(() => LoadGitFreshnessSnapshot(root, lines[0], git))).Value;
        if (snapshot.Count == 0) GitFreshnessSnapshots.TryRemove(key, out _);
        return snapshot;
    }

    private static IReadOnlyDictionary<string, (string Commit, DateTimeOffset Modified)> LoadGitFreshnessSnapshot(string root, string revision, GitClient git)
    {
        var files = new Dictionary<string, (string Commit, DateTimeOffset Modified)>(StringComparer.Ordinal);
        var log = git.RunRawAsync(root, new[] { "log", revision, "--format=%x00PF-COMMIT:%H%x09%cI%x00", "--name-only", "-z", "--no-renames", "--diff-merges=combined" }, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        if (!log.Succeeded) return files;
        string? commit = null;
        DateTimeOffset modified = default;
        foreach (var token in log.StdOut.Split('\0'))
        {
            var value = token.Trim('\r', '\n');
            if (value.StartsWith("PF-COMMIT:", StringComparison.Ordinal))
            {
                var header = value[10..].Split('\t');
                commit = header.Length == 2 && DateTimeOffset.TryParse(header[1], out modified) ? header[0] : null;
            }
            else if (commit is not null && value.Length > 0)
                files.TryAdd(value, (commit, modified));
        }
        return files;
    }
}
