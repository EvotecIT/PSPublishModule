using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Applies resolved project versions to explicitly configured repository files.
/// </summary>
internal sealed class ProjectVersionBindingService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);
    private readonly ILogger _logger;

    public ProjectVersionBindingService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<ProjectVersionBindingFileUpdate> Plan(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> resolvedVersions,
        IReadOnlyList<ProjectVersionBinding>? bindings,
        IReadOnlyDictionary<string, string>? plannedContentsByPath = null)
    {
        if (bindings is null || bindings.Count == 0)
            return Array.Empty<ProjectVersionBindingFileUpdate>();
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        if (resolvedVersions is null)
            throw new ArgumentNullException(nameof(resolvedVersions));

        var root = Path.GetFullPath(repositoryRoot);
        var comparison = FrameworkCompatibility.GetPathStringComparison(root);
        var comparer = comparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var plannedFiles = new Dictionary<string, PlannedFile>(comparer);

        foreach (var binding in bindings)
        {
            if (binding is null)
                throw new InvalidOperationException("VersionBindings cannot contain null entries.");

            var bindingPath = RequireValue(binding.Path, "VersionBindings.Path");
            var project = RequireValue(binding.Project, $"Version binding '{bindingPath}' Project");
            var pattern = RequireUntrimmedText(binding.Pattern, $"Version binding '{bindingPath}' Pattern");
            var replacement = RequireUntrimmedText(binding.Replacement, $"Version binding '{bindingPath}' Replacement");
            if (replacement.IndexOf("{Version}", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException($"Version binding '{bindingPath}' Replacement must contain '{{Version}}'.");
            if (Path.IsPathRooted(bindingPath))
                throw new InvalidOperationException($"Version binding path '{bindingPath}' must be repository-relative.");
            if (!resolvedVersions.TryGetValue(project, out var version) || string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException($"Version binding '{bindingPath}' references project '{project}', which has no resolved version.");

            var fullPath = Path.GetFullPath(Path.Combine(root, bindingPath));
            EnsureChildPath(root, fullPath, comparison, bindingPath);
            EnsureNoReparsePoints(root, fullPath, bindingPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"Version binding file was not found: {bindingPath}", fullPath);

            if (!plannedFiles.TryGetValue(fullPath, out var plannedFile))
            {
                var content = RepositoryTextFileTransactionService.ReadText(fullPath);
                var plannedContent = plannedContentsByPath is not null &&
                    plannedContentsByPath.TryGetValue(fullPath, out var overlaidContent)
                        ? overlaidContent
                        : content;
                plannedFile = new PlannedFile(content, plannedContent);
                plannedFiles.Add(fullPath, plannedFile);
            }

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"Version binding '{bindingPath}' has an invalid Pattern: {ex.Message}", ex);
            }

            var matches = regex.Matches(plannedFile.UpdatedContent);
            if (matches.Count != 1)
                throw new InvalidOperationException($"Version binding '{bindingPath}' Pattern must match exactly once; found {matches.Count} matches.");

            var replacementText = replacement.Replace("{Version}", version);
            plannedFile.UpdatedContent = regex.Replace(
                plannedFile.UpdatedContent,
                _ => replacementText,
                count: 1);
            plannedFile.BindingCount++;
        }

        return plannedFiles
            .OrderBy(static pair => pair.Key, comparer)
            .Select(pair => new ProjectVersionBindingFileUpdate(
                new RepositoryTextFileUpdate(pair.Key, pair.Value.OriginalContent, pair.Value.UpdatedContent),
                FrameworkCompatibility.GetRelativePath(root, pair.Key),
                pair.Value.BindingCount))
            .ToArray();
    }

    public void Apply(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> resolvedVersions,
        IReadOnlyList<ProjectVersionBinding>? bindings,
        bool whatIf,
        bool logChanges = true)
    {
        var plan = Plan(repositoryRoot, resolvedVersions, bindings);
        if (whatIf)
        {
            if (logChanges)
                LogPlanned(plan);
            return;
        }

        new RepositoryTextFileTransactionService().Apply(plan.Select(static item => item.Update).ToArray());
        if (logChanges)
            LogApplied(plan);
    }

    public void LogPlanned(IReadOnlyList<ProjectVersionBindingFileUpdate> plan)
    {
        foreach (var item in plan)
        {
            if (item.HasChanges)
                _logger.Info($"Version binding planned: {item.RelativePath} ({item.BindingCount} binding(s)).");
            else
                _logger.Info($"Version binding unchanged: {item.RelativePath}.");
        }
    }

    public void LogApplied(IReadOnlyList<ProjectVersionBindingFileUpdate> plan)
    {
        foreach (var item in plan)
        {
            if (item.HasChanges)
                _logger.Success($"Version binding updated: {item.RelativePath} ({item.BindingCount} binding(s)).");
            else
                _logger.Info($"Version binding unchanged: {item.RelativePath}.");
        }
    }

    private static string RequireValue(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");

        return value!.Trim();
    }

    private static string RequireUntrimmedText(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");

        return value!;
    }

    private static void EnsureChildPath(
        string root,
        string candidate,
        StringComparison comparison,
        string configuredPath)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, comparison))
            throw new InvalidOperationException($"Version binding path '{configuredPath}' must resolve inside the repository root.");
    }

    private static void EnsureNoReparsePoints(string root, string candidate, string configuredPath)
    {
        var relativePath = FrameworkCompatibility.GetRelativePath(root, candidate);
        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var currentPath = root;
        foreach (var segment in segments)
        {
            currentPath = Path.Combine(currentPath, segment);
            try
            {
                if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Version binding path '{configuredPath}' cannot traverse a symbolic link or junction.");
            }
            catch (FileNotFoundException)
            {
                // The final existence check provides the binding-specific error.
            }
            catch (DirectoryNotFoundException)
            {
                // The final existence check provides the binding-specific error.
            }
        }
    }

    private sealed class PlannedFile
    {
        public PlannedFile(string originalContent, string updatedContent)
        {
            OriginalContent = originalContent;
            UpdatedContent = updatedContent;
        }

        public string OriginalContent { get; }
        public string UpdatedContent { get; set; }
        public int BindingCount { get; set; }
    }
}

internal sealed class ProjectVersionBindingFileUpdate
{
    public ProjectVersionBindingFileUpdate(RepositoryTextFileUpdate update, string relativePath, int bindingCount)
    {
        Update = update;
        RelativePath = relativePath;
        BindingCount = bindingCount;
    }

    public RepositoryTextFileUpdate Update { get; }
    public string RelativePath { get; }
    public int BindingCount { get; }
    public bool HasChanges => !string.Equals(Update.OriginalContent, Update.UpdatedContent, StringComparison.Ordinal);
}
