using PowerForge;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static (string? Revision, bool WorkingTreeChanged) ResolveSourceSnapshot(
        string? root, string? pattern, IReadOnlyList<WebApiDocsSourceUrlMapping> mappings, List<string> warnings)
    {
        if (!(pattern?.Contains("{revision}", StringComparison.OrdinalIgnoreCase) == true ||
              mappings.Any(mapping => mapping.UrlPattern?.Contains("{revision}", StringComparison.OrdinalIgnoreCase) == true)))
            return (null, false);
        if (string.IsNullOrWhiteSpace(root)) return (null, false);
        try
        {
            var git = new GitClient(defaultTimeout: TimeSpan.FromSeconds(10));
            var revision = git.RunRawAsync(root, new[] { "rev-parse", "HEAD" }).GetAwaiter().GetResult();
            if (!revision.Succeeded || !System.Text.RegularExpressions.Regex.IsMatch(revision.StdOut.Trim(), "^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$"))
            {
                warnings.Add("API docs source: {revision} requires a Git source root; revision source URLs were omitted.");
                return (null, false);
            }
            var status = git.RunRawAsync(root, new[] { "status", "--porcelain", "--untracked-files=no" }).GetAwaiter().GetResult();
            return (revision.StdOut.Trim(), !status.Succeeded || !string.IsNullOrWhiteSpace(status.StdOut));
        }
        catch (Exception ex)
        {
            warnings.Add($"API docs source: could not resolve source revision ({ex.Message}); revision source URLs were omitted.");
            return (null, false);
        }
    }
}
