using PowerForge;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    private static void AppendGitFreshnessMetadata(
        IReadOnlyList<ApiTypeModel> types,
        WebApiDocsOptions options)
    {
        if (types is null || types.Count == 0 || options is null || !options.GenerateGitFreshness)
            return;

        var updatedDays = Math.Max(options.GitFreshnessNewDays, options.GitFreshnessUpdatedDays);
        var newDays = Math.Clamp(options.GitFreshnessNewDays, 0, updatedDays);
        var utcNow = DateTimeOffset.UtcNow;

        var gitClient = new GitClient(defaultTimeout: TimeSpan.FromSeconds(10));
        var repositoryRootCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var freshnessCache = new Dictionary<string, ApiFreshnessModel?>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var candidates = GetFreshnessCandidateFiles(type, options);
            ApiFreshnessModel? best = null;
            foreach (var candidate in candidates)
            {
                if (!freshnessCache.TryGetValue(candidate, out var freshness))
                {
                    freshness = TryGetGitFreshness(candidate, gitClient, repositoryRootCache, utcNow, newDays, updatedDays);
                    freshnessCache[candidate] = freshness;
                }

                if (freshness is null)
                    continue;

                if (best is null || freshness.LastModifiedUtc > best.LastModifiedUtc)
                    best = freshness;
            }

            type.Freshness = best;
        }
    }

    private static ApiFreshnessModel? TryGetGitFreshness(
        string filePath,
        GitClient gitClient,
        IDictionary<string, string?> repositoryRootCache,
        DateTimeOffset utcNow,
        int newDays,
        int updatedDays)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        if (!repositoryRootCache.TryGetValue(directory, out var repositoryRoot))
        {
            var topLevel = gitClient.ShowTopLevelAsync(directory).GetAwaiter().GetResult();
            repositoryRoot = topLevel.Succeeded
                ? topLevel.StdOut.Trim()
                : null;
            repositoryRootCache[directory] = repositoryRoot;
        }

        if (string.IsNullOrWhiteSpace(repositoryRoot) || !Directory.Exists(repositoryRoot))
            return null;

        var relativePath = Path.GetRelativePath(repositoryRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relativePath) ||
            relativePath.StartsWith("..", StringComparison.Ordinal))
            return null;

        var log = gitClient.RunRawAsync(
                repositoryRoot,
                new[] { "log", "-1", "--format=%H%n%cI", "--", relativePath.Replace('\\', '/') },
                TimeSpan.FromSeconds(10))
            .GetAwaiter()
            .GetResult();
        if (!log.Succeeded || string.IsNullOrWhiteSpace(log.StdOut))
            return null;

        var lines = log.StdOut
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2 || !DateTimeOffset.TryParse(lines[1], out var lastModified))
            return null;

        var ageDays = Math.Max(0, (int)Math.Floor((utcNow - lastModified).TotalDays));
        var status = ageDays <= newDays
            ? "new"
            : ageDays <= updatedDays
                ? "updated"
                : "stable";

        return new ApiFreshnessModel
        {
            Status = status,
            LastModifiedUtc = lastModified.ToUniversalTime(),
            CommitSha = string.IsNullOrWhiteSpace(lines[0]) ? null : lines[0].Trim(),
            AgeDays = ageDays,
            SourcePath = fullPath
        };
    }

    private static string[] GetFreshnessCandidateFiles(ApiTypeModel type, WebApiDocsOptions options)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (type is null)
            return Array.Empty<string>();

        AddFreshnessCandidate(files, type.Source?.Path, options.SourceRootPath);
        foreach (var originFile in type.OriginFiles)
            AddFreshnessCandidate(files, originFile, options.SourceRootPath);

        foreach (var member in type.Methods)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);
        foreach (var member in type.Constructors)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);
        foreach (var member in type.Properties)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);
        foreach (var member in type.Fields)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);
        foreach (var member in type.Events)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);
        foreach (var member in type.ExtensionMethods)
            AddFreshnessCandidate(files, member.Source?.Path, options.SourceRootPath);

        if (files.Count == 0)
        {
            if (options.Type == ApiDocsType.CSharp)
                AddFreshnessCandidate(files, options.XmlPath);
            else
                AddFreshnessCandidate(files, options.HelpPath);
        }

        return files.OrderBy(static file => file, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddFreshnessCandidate(ISet<string> files, string? path, string? sourceRootPath = null)
    {
        if (files is null || string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : !string.IsNullOrWhiteSpace(sourceRootPath)
                    ? Path.GetFullPath(Path.Combine(sourceRootPath, path))
                    : Path.GetFullPath(path);
            if (File.Exists(fullPath))
                files.Add(fullPath);
        }
        catch
        {
            // best effort only
        }
    }

    private static Dictionary<string, object?>? BuildFreshnessJson(ApiFreshnessModel? freshness)
    {
        if (freshness is null)
            return null;

        return new Dictionary<string, object?>
        {
            ["status"] = freshness.Status,
            ["lastModifiedUtc"] = freshness.LastModifiedUtc.ToString("O"),
            ["commitSha"] = freshness.CommitSha,
            ["ageDays"] = freshness.AgeDays,
            ["sourcePath"] = freshness.SourcePath
        };
    }
}
