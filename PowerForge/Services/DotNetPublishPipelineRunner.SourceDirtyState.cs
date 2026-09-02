namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Resolves the files and directories whose working-tree state can affect a release artifact.
    /// Dirty paths outside this closure are operator workspace state and do not invalidate source provenance.
    /// </summary>
    private static SourceDirtyScope BuildSourceDirtyScope(
        string projectRoot,
        string gitRoot,
        IEnumerable<string>? explicitInputPaths,
        IEnumerable<string>? sourceRootPaths,
        IEnumerable<string>? buildProjectPaths,
        string? buildConfiguration,
        DotNetPublishPlan? buildPlan)
    {
        var scope = new SourceDirtyScope();
        foreach (string path in explicitInputPaths ?? Array.Empty<string>())
            AddSourceDirtyScopePath(scope, projectRoot, gitRoot, path, directory: Directory.Exists(path));
        foreach (string path in sourceRootPaths ?? Array.Empty<string>())
            AddSourceDirtyScopePath(scope, projectRoot, gitRoot, path, directory: true);

        scope.BuildInputsResolved = TryEvaluateDotNetBuildInputs(
            buildProjectPaths,
            buildConfiguration,
            buildPlan,
            out string[] projectDirectories,
            out HashSet<string> buildInputs,
            out HashSet<string> sourceInputs,
            out NoBuildPublishInput[] noBuildPublishInputs);
        scope.ProjectDirectories = projectDirectories;
        scope.BuildInputs = buildInputs;
        scope.SourceInputs = sourceInputs;
        scope.NoBuildPublishInputs = noBuildPublishInputs;
        foreach (string directory in projectDirectories)
            AddProjectDirectoryScopePath(scope, projectRoot, gitRoot, directory);
        foreach (string path in buildInputs)
            AddSourceDirtyScopePath(scope, projectRoot, gitRoot, path, directory: false);
        return scope;
    }

    private static void AddProjectDirectoryScopePath(
        SourceDirtyScope scope,
        string projectRoot,
        string gitRoot,
        string? path)
    {
        if (TryGetGitScopePath(projectRoot, gitRoot, path, out string? relative))
            scope.ProjectDirectoryPaths.Add(relative!);
    }

    private static void AddSourceDirtyScopePath(
        SourceDirtyScope scope,
        string projectRoot,
        string gitRoot,
        string? path,
        bool directory)
    {
        if (!TryGetGitScopePath(projectRoot, gitRoot, path, out string? relative))
            return;

        if (directory)
            scope.DirectoryPaths.Add(relative!);
        else
            scope.FilePaths.Add(relative!);
    }

    private static bool TryGetGitScopePath(
        string projectRoot,
        string gitRoot,
        string? path,
        out string? relative)
    {
        relative = null;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string fullPath = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path));
        string fullGitRoot = Path.GetFullPath(gitRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(
                fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                fullGitRoot,
                comparison))
        {
            relative = string.Empty;
            return true;
        }

        relative = ToGitRelativeExclusion(projectRoot, gitRoot, fullPath)?
            .Replace('\\', '/')
            .Trim('/');
        return relative is not null;
    }

    private static string[]? FindTrackedSourceChanges(
        string projectRoot,
        string gitRoot,
        string? trackedOutput,
        IEnumerable<string>? generatedPaths,
        SourceDirtyScope scope)
    {
        if (trackedOutput is null || !TryParseTrackedStatusPaths(trackedOutput, out string[] changedPaths))
            return null;
        return FilterDirtySourcePaths(projectRoot, gitRoot, changedPaths, generatedPaths, scope);
    }

    private static string[] FindUntrackedSourceFiles(
        string projectRoot,
        string gitRoot,
        string? untrackedOutput,
        IEnumerable<string>? generatedPaths,
        SourceDirtyScope scope)
    {
        if (untrackedOutput is null)
            return Array.Empty<string>();
        string[] paths = untrackedOutput.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
        return FilterDirtySourcePaths(projectRoot, gitRoot, paths, generatedPaths, scope);
    }

    private static string[] FilterDirtySourcePaths(
        string projectRoot,
        string gitRoot,
        IEnumerable<string> paths,
        IEnumerable<string>? generatedPaths,
        SourceDirtyScope scope)
    {
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string[] exclusions = BuildGeneratedPathExclusions(projectRoot, gitRoot, generatedPaths);
        return paths
            .Select(path => path.Replace('\\', '/').TrimStart('/'))
            .Where(path => !IsGeneratedPath(path, exclusions, comparison))
            .Where(path => !scope.IsScoped || scope.Contains(path, gitRoot, comparison))
            .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderBy(path => path, IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class SourceDirtyScope
    {
        internal HashSet<string> FilePaths { get; } = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal HashSet<string> DirectoryPaths { get; } = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal HashSet<string> ProjectDirectoryPaths { get; } = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal bool BuildInputsResolved { get; set; }

        internal string[] ProjectDirectories { get; set; } = Array.Empty<string>();

        internal HashSet<string> BuildInputs { get; set; } = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal HashSet<string> SourceInputs { get; set; } = new(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        internal NoBuildPublishInput[] NoBuildPublishInputs { get; set; } =
            Array.Empty<NoBuildPublishInput>();

        internal bool IsScoped => DirectoryPaths.Count > 0 || ProjectDirectoryPaths.Count > 0;

        internal bool IsWithinProjectDirectory(string path, StringComparison comparison)
            => ProjectDirectoryPaths.Any(directory =>
                directory.Length == 0 ||
                string.Equals(path, directory, comparison) ||
                path.StartsWith(directory + "/", comparison));

        internal bool Contains(string path, string gitRoot, StringComparison comparison)
        {
            if (FilePaths.Contains(path))
                return true;
            if (DirectoryPaths.Any(directory =>
                directory.Length == 0 ||
                string.Equals(path, directory, comparison) ||
                path.StartsWith(directory + "/", comparison)))
            {
                return true;
            }

            string workingTreePath = Path.GetFullPath(Path.Combine(gitRoot, path.Replace('/', Path.DirectorySeparatorChar)));
            if (File.Exists(workingTreePath) || Directory.Exists(workingTreePath))
                return false;

            if (ProjectDirectoryPaths.Any(directory =>
                    directory.Length == 0 ||
                    string.Equals(path, directory, comparison) ||
                    path.StartsWith(directory + "/", comparison)))
            {
                return true;
            }

            string fileName = Path.GetFileName(path);
            if (!IsRepositoryBuildControlFile(fileName))
                return false;
            string directoryPath = Path.GetDirectoryName(path)?.Replace('\\', '/').Trim('/') ?? string.Empty;
            return ProjectDirectoryPaths.Any(projectDirectory =>
                directoryPath.Length == 0 ||
                string.Equals(projectDirectory, directoryPath, comparison) ||
                projectDirectory.StartsWith(directoryPath + "/", comparison));
        }

        private static bool IsRepositoryBuildControlFile(string fileName)
            => fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Directory.Build.rsp", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("MSBuild.rsp", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("global.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase);
    }
}
