using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Verifies repository dependency boundaries, shared-capability ownership, known consumers, and validation evidence.
/// </summary>
public sealed partial class RepositoryArchitectureService
{
    /// <summary>
    /// Loads an architecture policy from JSON.
    /// </summary>
    /// <param name="configPath">Architecture policy path.</param>
    /// <returns>Deserialized policy.</returns>
    public RepositoryArchitectureSpec Load(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Architecture config path is required.", nameof(configPath));

        var fullPath = Path.GetFullPath(configPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Architecture config was not found.", fullPath);

        var spec = JsonSerializer.Deserialize<RepositoryArchitectureSpec>(
            File.ReadAllText(fullPath),
            CreateJsonOptions());

        return spec ?? throw new InvalidOperationException($"Unable to deserialize architecture config: {fullPath}");
    }

    /// <summary>
    /// Verifies a repository against an architecture policy.
    /// </summary>
    /// <param name="spec">Architecture policy.</param>
    /// <param name="configPath">Policy path used to resolve relative paths.</param>
    /// <param name="changedFiles">Optional repository-relative changed files used for impact selection.</param>
    /// <returns>Architecture verification report.</returns>
    public RepositoryArchitectureReport Verify(
        RepositoryArchitectureSpec spec,
        string? configPath = null,
        IEnumerable<string>? changedFiles = null)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var issues = new List<RepositoryArchitectureIssue>();
        var repositoryRoot = ResolveRepositoryRoot(spec, configPath);
        var normalizedChangedFiles = NormalizeChangedFiles(repositoryRoot, changedFiles, issues);

        if (spec.SchemaVersion != 1)
        {
            AddError(issues, "ARC001", $"Unsupported architecture schemaVersion '{spec.SchemaVersion}'. Expected 1.");
        }

        var projects = DiscoverProjects(repositoryRoot, issues);
        var projectLookup = projects.ToDictionary(project => project.Path, StringComparer.OrdinalIgnoreCase);
        var projectReports = BuildProjectReports(projects);

        VerifyProjectRules(spec.ProjectRules, projectLookup, issues);

        var availableValidationSteps = ResolveValidationStepIds(spec, repositoryRoot, issues);
        var capabilityReports = VerifyCapabilities(
            spec,
            repositoryRoot,
            projects,
            projectLookup,
            normalizedChangedFiles,
            configPath,
            availableValidationSteps,
            issues);

        var requiredSteps = capabilityReports
            .Where(capability => capability.Impacted || normalizedChangedFiles.Length == 0)
            .SelectMany(capability => capability.RequiredValidationStepIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(step => step, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RepositoryArchitectureReport
        {
            Succeeded = issues.All(issue => issue.Severity != RepositoryArchitectureIssueSeverity.Error),
            RepositoryRoot = repositoryRoot,
            ConfigPath = string.IsNullOrWhiteSpace(configPath) ? null : Path.GetFullPath(configPath),
            ChangedFiles = normalizedChangedFiles,
            Projects = projectReports,
            Capabilities = capabilityReports,
            RequiredValidationStepIds = requiredSteps,
            Issues = issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .ThenBy(issue => issue.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(issue => issue.Message, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string ResolveRepositoryRoot(RepositoryArchitectureSpec spec, string? configPath)
    {
        var configDirectory = string.IsNullOrWhiteSpace(configPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(configPath!)) ?? Directory.GetCurrentDirectory();
        var candidate = string.IsNullOrWhiteSpace(spec.RepositoryRoot)
            ? configDirectory
            : Path.Combine(configDirectory, spec.RepositoryRoot!);
        var root = Path.GetFullPath(candidate);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Architecture repository root was not found: {root}");

        return root;
    }

    private static string[] ResolveValidationStepIds(
        RepositoryArchitectureSpec spec,
        string repositoryRoot,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(spec.WorkspaceValidationConfig))
            return Array.Empty<string>();

        string workspaceConfigPath;
        try
        {
            workspaceConfigPath = ResolveRepositoryPath(repositoryRoot, spec.WorkspaceValidationConfig!);
        }
        catch (Exception ex)
        {
            AddError(issues, "ARC300", ex.Message, spec.WorkspaceValidationConfig);
            return Array.Empty<string>();
        }

        if (!File.Exists(workspaceConfigPath))
        {
            AddError(issues, "ARC301", "Workspace validation config was not found.", ToRelativePath(repositoryRoot, workspaceConfigPath));
            return Array.Empty<string>();
        }

        try
        {
            var workspaceSpec = JsonSerializer.Deserialize<WorkspaceValidationSpec>(
                File.ReadAllText(workspaceConfigPath),
                CreateJsonOptions());
            if (workspaceSpec is null)
                throw new InvalidOperationException("The workspace validation config is empty.");

            var request = new WorkspaceValidationRequest
            {
                ProfileName = string.IsNullOrWhiteSpace(spec.WorkspaceValidationProfile)
                    ? "architecture"
                    : spec.WorkspaceValidationProfile
            };
            var plan = new WorkspaceValidationService().Plan(workspaceSpec, workspaceConfigPath, request);
            return plan.Steps
                .Select(step => step.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            AddError(
                issues,
                "ARC302",
                $"Workspace validation config could not produce profile '{spec.WorkspaceValidationProfile}': {ex.Message}",
                ToRelativePath(repositoryRoot, workspaceConfigPath));
            return Array.Empty<string>();
        }
    }
}
