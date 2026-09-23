using System.Text;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Domain.Projects;
using PowerForgeStudio.Orchestrator.Explorer;

namespace PowerForgeStudio.Orchestrator.Projects;

/// <summary>Builds a bounded, read-only summary of one selected project and working copy.</summary>
public sealed class ProjectOverviewService : IProjectOverviewService
{
    private const long MaxReadmeBytes = 256 * 1024;
    private const int MaxFiles = 400;
    private static readonly IReadOnlySet<string> ExcludedFolders = new FileExplorerOptions().ExcludedFolders;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public Task<ProjectOverviewSnapshot> InspectAsync(
        RepositoryCatalogEntry repository,
        string workingCopyRoot,
        ProjectGitStatus git,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingCopyRoot);
        ArgumentNullException.ThrowIfNull(git);
        var root = Path.GetFullPath(workingCopyRoot);
        return Task.Run(() => Inspect(repository, root, git, cancellationToken), cancellationToken);
    }

    private static ProjectOverviewSnapshot Inspect(
        RepositoryCatalogEntry repository,
        string workingCopyRoot,
        ProjectGitStatus git,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(workingCopyRoot)) throw new DirectoryNotFoundException(workingCopyRoot);

        var warnings = new List<string>();
        var files = EnumerateProjectFiles(workingCopyRoot, warnings, cancellationToken);
        var readme = files.FirstOrDefault(path =>
            PathComparer.Equals(Path.GetDirectoryName(path)!, workingCopyRoot) &&
            string.Equals(Path.GetFileName(path), "README.md", StringComparison.OrdinalIgnoreCase));
        var purpose = ReadPurpose(readme, warnings);
        var entryPoints = BuildEntryPoints(repository, workingCopyRoot, files);
        var products = BuildProducts(repository, files);
        var prerequisites = BuildPrerequisites(entryPoints, files, git);

        if (readme is null) warnings.Add("No root README.md was found; purpose remains unclassified.");
        if (entryPoints.Count == 0) warnings.Add("No supported build entrypoint was detected. Configure a project mapping before using Build & Run.");

        return new ProjectOverviewSnapshot(
            DateTimeOffset.UtcNow,
            repository.Name,
            repository.RepositoryKind.ToString(),
            !git.IsGitRepository ? "Local project"
                : SamePath(repository.RootPath, workingCopyRoot) ? WorkspaceKindDisplay(repository.WorkspaceKind) : "Worktree",
            repository.RootPath,
            workingCopyRoot,
            purpose,
            readme,
            git.IsGitRepository ? git.BranchDisplay : "Local project",
            git.IsGitRepository ? git.StatusSummary : "No Git working copy",
            git.AheadBehindDisplay,
            Math.Max(1, git.Worktrees.Count),
            products,
            entryPoints,
            prerequisites,
            warnings);
    }

    private static IReadOnlyList<string> EnumerateProjectFiles(
        string root,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var observedFiles = 0;
        var fileLimitReached = false;
        AddFiles(root);
        var directories = new List<string>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ExcludedFolders.Contains(Path.GetFileName(directory))) directories.Add(directory);
                if (directories.Count >= 80) break;
            }
            directories.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(left), Path.GetFileName(right)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Some project directories could not be listed: " + ex.Message);
        }
        foreach (var directory in directories)
        {
            if (fileLimitReached) break;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var attributes = File.GetAttributes(directory);
                if ((attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not inspect {Path.GetFileName(directory)}: {ex.Message}");
                continue;
            }
            AddFiles(directory);
        }
        if (fileLimitReached) warnings.Add($"Project signal scan stopped after inspecting {MaxFiles} file entries.");
        return files;

        void AddFiles(string directory)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (observedFiles >= MaxFiles)
                    {
                        fileLimitReached = true;
                        return;
                    }
                    observedFiles++;
                    try
                    {
                        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0) files.Add(Path.GetFullPath(file));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add($"Could not inspect {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not list {Path.GetFileName(directory)}: {ex.Message}");
                return;
            }
        }
    }

    private static string ReadPurpose(string? readme, ICollection<string> warnings)
    {
        if (readme is null) return "Purpose is not documented in a root README.md.";
        try
        {
            var info = new FileInfo(readme);
            if (info.Length > MaxReadmeBytes)
            {
                warnings.Add("README.md exceeds the 256 KiB overview limit; open the file to inspect it.");
                return "README.md is too large for the bounded overview preview.";
            }
            var lines = File.ReadAllLines(readme, Encoding.UTF8);
            var paragraph = lines
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#') && !line.StartsWith("[!", StringComparison.Ordinal))
                .Take(4);
            var text = string.Join(' ', paragraph);
            return text.Length == 0 ? "README.md does not contain a summary paragraph." : Bound(text, 600);
        }
        catch (DecoderFallbackException)
        {
            warnings.Add("README.md encoding is not supported by the overview preview.");
            return "README.md could not be decoded. Open the file to inspect it.";
        }
        catch (IOException ex)
        {
            warnings.Add("README.md could not be read: " + ex.Message);
            return "README.md is currently unavailable.";
        }
        catch (UnauthorizedAccessException ex)
        {
            warnings.Add("README.md could not be read: " + ex.Message);
            return "README.md is currently unavailable.";
        }
    }

    private static IReadOnlyList<ProjectOverviewItem> BuildEntryPoints(
        RepositoryCatalogEntry repository,
        string workingCopyRoot,
        IReadOnlyList<string> files)
    {
        var result = new List<ProjectOverviewItem>();
        AddConfigured(repository.UnifiedReleaseConfigPath, "Unified release", "Declarative PowerForge release contract");
        AddConfigured(repository.ModuleBuildScriptPath, "Module build", KindDetail(repository.ModuleBuildScriptPath));
        AddConfigured(repository.ProjectBuildScriptPath, "Project build", KindDetail(repository.ProjectBuildScriptPath));

        foreach (var path in files.Where(path => PathComparer.Equals(Path.GetDirectoryName(path)!, workingCopyRoot)))
        {
            var name = Path.GetFileName(path);
            if (name.Equals("build.ps1", StringComparison.OrdinalIgnoreCase))
                Add("Website build", "PowerShell website entrypoint", path);
            else if (HasExtension(path, ".sln", ".slnx"))
                Add(".NET solution", "Solution entrypoint", path);
        }
        return result;

        void AddConfigured(string? configuredPath, string name, string detail)
        {
            if (string.IsNullOrWhiteSpace(configuredPath)) return;
            var mapped = MapToWorkingCopy(repository.RootPath, workingCopyRoot, configuredPath);
            Add(name, detail, mapped);
        }

        void Add(string name, string detail, string path)
        {
            if (result.Any(item => item.SourcePath is not null && PathComparer.Equals(item.SourcePath, path))) return;
            result.Add(new ProjectOverviewItem(
                name,
                File.Exists(path) ? detail : detail + " (missing in selected working copy)",
                path,
                RelativeDisplay(workingCopyRoot, path)));
        }
    }

    private static IReadOnlyList<ProjectOverviewItem> BuildProducts(RepositoryCatalogEntry repository, IReadOnlyList<string> files)
    {
        var products = new List<ProjectOverviewItem>();
        if (repository.RepositoryKind is ReleaseRepositoryKind.Module or ReleaseRepositoryKind.Mixed)
            products.Add(new("PowerShell module", "Module packaging contract detected."));
        if (repository.RepositoryKind is ReleaseRepositoryKind.Library or ReleaseRepositoryKind.Mixed)
            products.Add(new(".NET / package", "Project packaging contract detected."));
        if (repository.HasWebsiteSignals)
            products.Add(new("Website", "Website build signals detected."));
        var projects = files.Count(path => HasExtension(path, ".csproj", ".fsproj", ".vbproj"));
        if (projects > 0) products.Add(new(".NET projects", $"{projects} project file(s) observed within the bounded scan."));
        if (products.Count == 0) products.Add(new("Unclassified", "No supported product contract was detected."));
        return products;
    }

    private static IReadOnlyList<ProjectOverviewItem> BuildPrerequisites(
        IReadOnlyList<ProjectOverviewItem> entryPoints,
        IReadOnlyList<string> files,
        ProjectGitStatus git)
    {
        var prerequisites = new List<ProjectOverviewItem>
        {
            new("Git", git.IsGitRepository ? "Available for the selected working copy." :
                "Not configured; local files and builds remain available, while Git workflows require a repository.")
        };
        if (entryPoints.Any(item => item.SourcePath?.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) == true))
            prerequisites.Add(new("PowerShell", "Required by a detected PowerShell entrypoint; verify the runtime under Connections."));
        if (entryPoints.Any(item => item.SourcePath?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true))
            prerequisites.Add(new("PowerForge", "Required to validate and execute the detected declarative contract."));
        if (files.Any(path => HasExtension(path, ".sln", ".slnx", ".csproj", ".fsproj", ".vbproj")))
            prerequisites.Add(new(".NET SDK", "Required by observed solution or project files; verify the runtime under Connections."));
        return prerequisites;
    }

    private static string MapToWorkingCopy(string projectRoot, string workingCopyRoot, string configuredPath)
    {
        var source = Path.GetFullPath(configuredPath);
        var relative = Path.GetRelativePath(Path.GetFullPath(projectRoot), source);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return source;
        return Path.GetFullPath(Path.Combine(workingCopyRoot, relative));
    }

    private static string KindDetail(string? path)
        => path?.EndsWith(".json", StringComparison.OrdinalIgnoreCase) == true
            ? "Declarative JSON configuration"
            : "PowerShell entrypoint";

    private static string Bound(string value, int length)
        => value.Length <= length ? value : value[..length] + "…";

    private static bool HasExtension(string path, params string[] extensions)
        => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static string RelativeDisplay(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? path
            : relative;
    }

    private static string WorkspaceKindDisplay(ReleaseWorkspaceKind kind) => kind switch
    {
        ReleaseWorkspaceKind.PrimaryRepository => "Primary repository",
        ReleaseWorkspaceKind.ReviewClone => "Review clone",
        ReleaseWorkspaceKind.TemporaryClone => "Temporary clone",
        _ => kind.ToString()
    };

    private static bool SamePath(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
