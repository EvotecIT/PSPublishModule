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
        var candidates = types.ToDictionary(type => type, type => GetFreshnessCandidateFiles(type, options));
        var repositoryRoots = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var filesByRepository = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in candidates.Values.SelectMany(files => files).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(file);
            while (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(Path.Combine(directory, ".git")) && !File.Exists(Path.Combine(directory, ".git")))
                directory = Path.GetDirectoryName(directory);
            if (string.IsNullOrWhiteSpace(directory)) continue;
            if (!repositoryRoots.TryGetValue(directory, out var repositoryRoot))
            {
                var topLevel = gitClient.ShowTopLevelAsync(directory).GetAwaiter().GetResult();
                repositoryRoot = topLevel.Succeeded ? topLevel.StdOut.Trim() : null;
                repositoryRoots[directory] = repositoryRoot;
            }
            if (string.IsNullOrWhiteSpace(repositoryRoot)) continue;
            if (!filesByRepository.TryGetValue(repositoryRoot, out var files)) filesByRepository[repositoryRoot] = files = new List<string>();
            files.Add(file);
        }

        var freshness = new Dictionary<string, ApiFreshnessModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var repository in filesByRepository)
        {
            var history = GetGitFreshnessSnapshot(repository.Key, gitClient);
            foreach (var file in repository.Value)
            {
                var relative = Path.GetRelativePath(repository.Key, file).Replace('\\', '/');
                if (!history.TryGetValue(relative, out var item)) continue;
                var age = Math.Max(0, (int)Math.Floor((utcNow - item.Modified).TotalDays));
                freshness[file] = new ApiFreshnessModel
                {
                    Status = age <= newDays ? "new" : age <= updatedDays ? "updated" : "stable",
                    LastModifiedUtc = item.Modified.ToUniversalTime(), CommitSha = item.Commit, AgeDays = age, SourcePath = file
                };
            }
        }
        foreach (var type in types)
            type.Freshness = candidates[type].Where(freshness.ContainsKey).Select(file => freshness[file])
                .OrderByDescending(item => item.LastModifiedUtc).FirstOrDefault();
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
            {
                AddFreshnessCandidate(files, options.XmlPath);
                foreach (var xmlPath in options.XmlPaths)
                    AddFreshnessCandidate(files, xmlPath);
            }
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

    private static Dictionary<string, object?>? BuildFreshnessJson(ApiFreshnessModel? freshness, WebApiDocsOptions options)
    {
        if (freshness is null)
            return null;

        return new Dictionary<string, object?>
        {
            ["status"] = freshness.Status,
            ["lastModifiedUtc"] = freshness.LastModifiedUtc.ToString("O"),
            ["commitSha"] = freshness.CommitSha,
            ["ageDays"] = freshness.AgeDays,
            ["sourcePath"] = NormalizeFreshnessSourcePath(freshness.SourcePath, options)
        };
    }

    private static string? NormalizeFreshnessSourcePath(string? path, WebApiDocsOptions? options)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            foreach (var root in EnumerateFreshnessNormalizationRoots(options))
            {
                var relativePath = TryGetRelativePathWithinRoot(root, fullPath);
                if (!string.IsNullOrWhiteSpace(relativePath))
                    return relativePath;
            }

            return Path.GetFileName(fullPath);
        }
        catch
        {
            return Path.GetFileName(path);
        }
    }

    private static IEnumerable<string> EnumerateFreshnessNormalizationRoots(WebApiDocsOptions? options)
    {
        if (options is null)
            yield break;

        foreach (var root in EnumerateNonEmptyDirectories(
                     options.SourceRootPath,
                     GetParentDirectory(options.HelpPath),
                     GetParentDirectory(options.XmlPath)))
        {
            yield return root;
        }
    }
}
