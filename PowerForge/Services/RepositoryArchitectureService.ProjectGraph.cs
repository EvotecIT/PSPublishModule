using System.Xml.Linq;

namespace PowerForge;

public sealed partial class RepositoryArchitectureService
{
    private sealed class DiscoveredProject
    {
        internal string Path { get; set; } = string.Empty;
        internal string FullPath { get; set; } = string.Empty;
        internal string DirectoryPath { get; set; } = string.Empty;
        internal bool IsTestProject { get; set; }
        internal string[] ProjectReferences { get; set; } = Array.Empty<string>();
        internal string[] PackageReferences { get; set; } = Array.Empty<string>();
    }

    private static DiscoveredProject[] DiscoverProjects(
        string repositoryRoot,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var projects = new List<DiscoveredProject>();
        var projectFiles = EnumerateRepositoryFiles(repositoryRoot)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var projectFile in projectFiles)
        {
            var relativeProjectPath = ToRelativePath(repositoryRoot, projectFile);
            try
            {
                var document = XDocument.Load(projectFile);
                var projectDirectory = Path.GetDirectoryName(projectFile) ?? repositoryRoot;
                var projectReferences = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "ProjectReference")
                    .Select(element => (string?)element.Attribute("Include"))
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
                    .Select(path =>
                    {
                        EnsureInsideRoot(repositoryRoot, path, path);
                        return path;
                    })
                    .Select(path => ToRelativePath(repositoryRoot, path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var packageReferences = document
                    .Descendants()
                    .Where(element => element.Name.LocalName == "PackageReference")
                    .Select(element => (string?)element.Attribute("Include") ?? (string?)element.Attribute("Update"))
                    .Where(include => !string.IsNullOrWhiteSpace(include))
                    .Select(include => include!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(package => package, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var isTestProperty = document
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "IsTestProject")
                    ?.Value;
                var projectName = Path.GetFileNameWithoutExtension(projectFile);
                var isTestProject = string.Equals(isTestProperty, "true", StringComparison.OrdinalIgnoreCase)
                                    || projectName.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
                                    || projectName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase)
                                    || packageReferences.Contains("Microsoft.NET.Test.Sdk", StringComparer.OrdinalIgnoreCase);

                projects.Add(new DiscoveredProject
                {
                    Path = relativeProjectPath,
                    FullPath = projectFile,
                    DirectoryPath = NormalizePath(ToRelativePath(repositoryRoot, projectDirectory)),
                    IsTestProject = isTestProject,
                    ProjectReferences = projectReferences,
                    PackageReferences = packageReferences
                });
            }
            catch (Exception ex)
            {
                AddError(issues, "ARC010", $"Project file could not be inspected: {ex.Message}", relativeProjectPath);
            }
        }

        return projects.ToArray();
    }

    private static RepositoryArchitectureProject[] BuildProjectReports(IReadOnlyCollection<DiscoveredProject> projects)
    {
        return projects
            .Select(project => new RepositoryArchitectureProject
            {
                Id = project.Path,
                Path = project.Path,
                IsTestProject = project.IsTestProject,
                ProjectReferences = project.ProjectReferences,
                PackageReferences = project.PackageReferences,
                ReverseProjectReferences = projects
                    .Where(candidate => candidate.ProjectReferences.Contains(project.Path, StringComparer.OrdinalIgnoreCase))
                    .Select(candidate => candidate.Path)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            })
            .OrderBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void VerifyProjectRules(
        IEnumerable<RepositoryArchitectureProjectRule>? rules,
        IReadOnlyDictionary<string, DiscoveredProject> projectLookup,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var seenRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules ?? Array.Empty<RepositoryArchitectureProjectRule>())
        {
            var ruleId = (rule.Id ?? string.Empty).Trim();
            var projectPath = NormalizePath(rule.Project);
            if (ruleId.Length == 0)
            {
                AddError(issues, "ARC100", "A project rule has an empty id.", projectPath);
                continue;
            }
            if (!seenRuleIds.Add(ruleId))
                AddError(issues, "ARC101", $"Duplicate project rule id '{ruleId}'.", projectPath, projectId: ruleId);
            if (!seenProjects.Add(projectPath))
                AddError(issues, "ARC102", $"Project '{projectPath}' has more than one architecture rule.", projectPath, projectId: ruleId);

            if (!projectLookup.TryGetValue(projectPath, out var project))
            {
                AddError(issues, "ARC103", $"Configured project '{projectPath}' was not found.", projectPath, projectId: ruleId);
                continue;
            }

            VerifyReferencePolicy(
                rule.AllowedProjectReferences,
                rule.RequiredProjectReferences,
                rule.ForbiddenProjectReferences,
                project.ProjectReferences,
                "project",
                ruleId,
                projectPath,
                issues);
            VerifyReferencePolicy(
                rule.AllowedPackageReferences,
                rule.RequiredPackageReferences,
                rule.ForbiddenPackageReferences,
                project.PackageReferences,
                "package",
                ruleId,
                projectPath,
                issues);
        }
    }

    private static void VerifyReferencePolicy(
        string[]? allowedValues,
        string[]? requiredValues,
        string[]? forbiddenValues,
        IEnumerable<string> actualValues,
        string referenceKind,
        string ruleId,
        string projectPath,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var actual = actualValues.Select(NormalizeReference).ToArray();
        if (allowedValues is not null)
        {
            var allowed = new HashSet<string>(allowedValues.Select(NormalizeReference), StringComparer.OrdinalIgnoreCase);
            foreach (var unexpected in actual.Where(value => !allowed.Contains(value)))
            {
                AddError(
                    issues,
                    referenceKind == "project" ? "ARC110" : "ARC120",
                    $"Project rule '{ruleId}' does not allow {referenceKind} reference '{unexpected}'.",
                    projectPath,
                    projectId: ruleId);
            }
        }

        var required = new HashSet<string>(
            (requiredValues ?? Array.Empty<string>()).Select(NormalizeReference),
            StringComparer.OrdinalIgnoreCase);
        foreach (var missing in required.Where(value => !actual.Contains(value, StringComparer.OrdinalIgnoreCase)))
        {
            AddError(
                issues,
                referenceKind == "project" ? "ARC112" : "ARC122",
                $"Project rule '{ruleId}' requires missing {referenceKind} reference '{missing}'.",
                projectPath,
                projectId: ruleId);
        }

        var forbidden = new HashSet<string>(
            (forbiddenValues ?? Array.Empty<string>()).Select(NormalizeReference),
            StringComparer.OrdinalIgnoreCase);
        foreach (var blocked in actual.Where(forbidden.Contains))
        {
            AddError(
                issues,
                referenceKind == "project" ? "ARC111" : "ARC121",
                $"Project rule '{ruleId}' forbids {referenceKind} reference '{blocked}'.",
                projectPath,
                projectId: ruleId);
        }
    }

    private static string NormalizeReference(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            ? NormalizePath(normalized)
            : normalized;
    }
}
