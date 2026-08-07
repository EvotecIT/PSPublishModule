namespace PowerForge;

public sealed partial class RepositoryArchitectureService
{
    private static RepositoryArchitectureCapabilityResult[] VerifyCapabilities(
        RepositoryArchitectureSpec spec,
        string repositoryRoot,
        IReadOnlyCollection<DiscoveredProject> projects,
        IReadOnlyDictionary<string, DiscoveredProject> projectLookup,
        IReadOnlyCollection<string> changedFiles,
        string? configPath,
        IReadOnlyCollection<string> availableValidationSteps,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var results = new List<RepositoryArchitectureCapabilityResult>();
        var capabilityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globalImpactPaths = new List<string>(spec.GlobalImpactPaths ?? Array.Empty<string>());
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            try
            {
                globalImpactPaths.Add(ToRelativePath(repositoryRoot, Path.GetFullPath(configPath!)));
            }
            catch
            {
                // Config path validation is reported by the caller; it cannot add an in-repository impact path here.
            }
        }
        if (!string.IsNullOrWhiteSpace(spec.WorkspaceValidationConfig))
            globalImpactPaths.Add(NormalizePath(spec.WorkspaceValidationConfig));
        var hasGlobalImpact = changedFiles.Any(path => MatchesAny(path, globalImpactPaths));

        foreach (var capability in spec.Capabilities ?? Array.Empty<RepositoryArchitectureCapability>())
        {
            var capabilityId = (capability.Id ?? string.Empty).Trim();
            if (capabilityId.Length == 0)
            {
                AddError(issues, "ARC200", "A capability has an empty id.");
                continue;
            }
            if (!capabilityIds.Add(capabilityId))
                AddError(issues, "ARC201", $"Duplicate capability id '{capabilityId}'.", capabilityId: capabilityId);

            var ownerProjects = NormalizeDistinct(capability.OwnerProjects);
            var declaredConsumers = NormalizeDistinct(capability.ConsumerProjects);
            var ignoredUsageProjects = new HashSet<string>(
                NormalizeDistinct(capability.IgnoredUsageProjects),
                StringComparer.OrdinalIgnoreCase);

            VerifyConfiguredProjects(ownerProjects, "owner", capabilityId, projectLookup, issues);
            VerifyConfiguredProjects(declaredConsumers, "consumer", capabilityId, projectLookup, issues);
            foreach (var overlap in ownerProjects.Intersect(declaredConsumers, StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    issues,
                    "ARC204",
                    $"Project '{overlap}' cannot be both owner and consumer of capability '{capabilityId}'.",
                    overlap,
                    capabilityId);
            }

            var ownerPaths = capability.OwnerPaths ?? Array.Empty<string>();
            if (ownerPaths.Length == 0)
            {
                AddError(
                    issues,
                    "ARC206",
                    $"Capability '{capabilityId}' must declare at least one owner path.",
                    capabilityId: capabilityId);
            }
            foreach (var ownerPath in ownerPaths)
            {
                if (!EnumerateMatchingPaths(repositoryRoot, ownerPath).Any())
                {
                    AddError(
                        issues,
                        "ARC205",
                        $"Owner path pattern '{ownerPath}' does not match any repository file.",
                        ownerPath,
                        capabilityId);
                }
            }

            var observedConsumers = DiscoverUsageProjects(
                capability,
                repositoryRoot,
                projects,
                ownerProjects,
                ignoredUsageProjects,
                capabilityId,
                issues);

            foreach (var undeclared in observedConsumers.Except(declaredConsumers, StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    issues,
                    "ARC210",
                    $"Capability '{capabilityId}' is used by undeclared consumer project '{undeclared}'.",
                    undeclared,
                    capabilityId);
            }

            if (capability.RequireObservedConsumers && (capability.UsagePatterns?.Length ?? 0) > 0)
            {
                foreach (var unobserved in declaredConsumers.Except(observedConsumers, StringComparer.OrdinalIgnoreCase))
                {
                    AddError(
                        issues,
                        "ARC211",
                        $"Declared consumer '{unobserved}' does not contain a configured usage pattern for capability '{capabilityId}'.",
                        unobserved,
                        capabilityId);
                }
            }

            var evidence = capability.Evidence ?? Array.Empty<RepositoryArchitectureEvidence>();
            if (evidence.Length == 0)
            {
                AddError(
                    issues,
                    "ARC229",
                    $"Capability '{capabilityId}' must declare at least one evidence item.",
                    capabilityId: capabilityId);
            }
            VerifyEvidence(
                capability,
                capabilityId,
                repositoryRoot,
                ownerProjects,
                declaredConsumers,
                evidence,
                availableValidationSteps,
                spec.WorkspaceValidationConfig,
                spec.WorkspaceValidationProfile,
                issues);

            var impacted = hasGlobalImpact
                           || changedFiles.Any(path => MatchesAny(path, ownerPaths))
                           || changedFiles.Any(path => EvidenceContainsPath(path, evidence))
                           || changedFiles.Any(path => IsInsideAnyProject(path, ownerProjects.Concat(declaredConsumers), projectLookup));

            results.Add(new RepositoryArchitectureCapabilityResult
            {
                Id = capabilityId,
                Impacted = impacted,
                OwnerProjects = ownerProjects,
                DeclaredConsumerProjects = declaredConsumers,
                ObservedConsumerProjects = observedConsumers,
                RequiredEvidenceIds = evidence
                    .Select(item => item.Id)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                RequiredValidationStepIds = evidence
                    .Select(item => item.StepId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            });
        }

        return results.OrderBy(result => result.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void VerifyConfiguredProjects(
        IEnumerable<string> configuredProjects,
        string role,
        string capabilityId,
        IReadOnlyDictionary<string, DiscoveredProject> projectLookup,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        foreach (var project in configuredProjects)
        {
            if (!projectLookup.ContainsKey(project))
            {
                AddError(
                    issues,
                    role == "owner" ? "ARC202" : "ARC203",
                    $"Configured {role} project '{project}' for capability '{capabilityId}' was not found.",
                    project,
                    capabilityId);
            }
        }
    }

    private static string[] DiscoverUsageProjects(
        RepositoryArchitectureCapability capability,
        string repositoryRoot,
        IReadOnlyCollection<DiscoveredProject> projects,
        IReadOnlyCollection<string> ownerProjects,
        ISet<string> ignoredUsageProjects,
        string capabilityId,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        var patterns = (capability.UsagePatterns ?? Array.Empty<string>())
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToArray();
        if (patterns.Length == 0)
            return Array.Empty<string>();

        var comparison = capability.UsagePatternCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = EnumerateRepositoryFiles(repositoryRoot)
            .Select(path => new { FullPath = path, RelativePath = ToRelativePath(repositoryRoot, path) })
            .Where(item => MatchesAny(item.RelativePath, capability.UsagePathIncludes))
            .Where(item => !MatchesAny(item.RelativePath, capability.UsagePathExcludes));

        foreach (var sourceFile in sourceFiles)
        {
            string text;
            try
            {
                text = File.ReadAllText(sourceFile.FullPath);
            }
            catch (Exception ex)
            {
                AddError(
                    issues,
                    "ARC212",
                    $"Source usage file could not be inspected: {ex.Message}",
                    sourceFile.RelativePath,
                    capabilityId);
                continue;
            }

            if (!patterns.Any(pattern => text.IndexOf(pattern, comparison) >= 0))
                continue;

            var project = FindContainingProject(sourceFile.RelativePath, projects);
            if (project is null || project.IsTestProject)
                continue;
            if (ownerProjects.Contains(project.Path, StringComparer.OrdinalIgnoreCase))
                continue;
            if (ignoredUsageProjects.Contains(project.Path))
                continue;

            observed.Add(project.Path);
        }

        return observed.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static DiscoveredProject? FindContainingProject(
        string relativeFilePath,
        IEnumerable<DiscoveredProject> projects)
    {
        var normalizedFile = NormalizePath(relativeFilePath);
        return projects
            .Where(project => IsPathInside(normalizedFile, project.DirectoryPath))
            .OrderByDescending(project => project.DirectoryPath.Length)
            .ThenBy(project => project.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static void VerifyEvidence(
        RepositoryArchitectureCapability capability,
        string capabilityId,
        string repositoryRoot,
        IReadOnlyCollection<string> ownerProjects,
        IReadOnlyCollection<string> consumerProjects,
        IReadOnlyCollection<RepositoryArchitectureEvidence> evidence,
        IReadOnlyCollection<string> availableValidationSteps,
        string? workspaceValidationConfig,
        string? workspaceValidationProfile,
        ICollection<RepositoryArchitectureIssue> issues)
    {
        if (evidence.Count > 0 && string.IsNullOrWhiteSpace(workspaceValidationConfig))
        {
            AddError(
                issues,
                "ARC227",
                $"Capability '{capabilityId}' declares executable evidence but the policy has no workspaceValidationConfig.",
                capabilityId: capabilityId);
        }

        var evidenceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in evidence)
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                AddError(issues, "ARC220", $"Capability '{capabilityId}' has evidence with an empty id.", capabilityId: capabilityId);
                continue;
            }
            if (!evidenceIds.Add(item.Id))
                AddError(issues, "ARC221", $"Capability '{capabilityId}' has duplicate evidence id '{item.Id}'.", capabilityId: capabilityId);

            if (string.IsNullOrWhiteSpace(item.Kind))
                AddError(issues, "ARC228", $"Evidence '{item.Id}' has an empty kind.", item.Path, capabilityId);

            if (!string.IsNullOrWhiteSpace(item.Path))
            {
                try
                {
                    var fullEvidencePath = ResolveRepositoryPath(repositoryRoot, item.Path!);
                    if (!File.Exists(fullEvidencePath) && !Directory.Exists(fullEvidencePath))
                    {
                        AddError(
                            issues,
                            "ARC222",
                            $"Evidence '{item.Id}' path was not found.",
                            item.Path,
                            capabilityId);
                    }
                }
                catch (Exception ex)
                {
                    AddError(issues, "ARC222", ex.Message, item.Path, capabilityId);
                }
            }

            if (string.IsNullOrWhiteSpace(item.StepId))
            {
                AddError(issues, "ARC223", $"Evidence '{item.Id}' does not declare a workspace validation stepId.", item.Path, capabilityId);
            }
            else if (!string.IsNullOrWhiteSpace(workspaceValidationConfig)
                     && !availableValidationSteps.Contains(item.StepId, StringComparer.OrdinalIgnoreCase))
            {
                AddError(
                    issues,
                    "ARC224",
                    $"Evidence '{item.Id}' references validation step '{item.StepId}', but that step is not active in profile '{workspaceValidationProfile}'.",
                    item.Path,
                    capabilityId);
            }
        }

        var evidenceKinds = new HashSet<string>(
            evidence.Select(item => item.Kind).Where(kind => !string.IsNullOrWhiteSpace(kind)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var requiredKind in capability.RequiredEvidenceKinds ?? Array.Empty<string>())
        {
            if (!evidenceKinds.Contains(requiredKind))
            {
                AddError(
                    issues,
                    "ARC225",
                    $"Capability '{capabilityId}' requires evidence kind '{requiredKind}', but none is declared.",
                    capabilityId: capabilityId);
            }
        }

        var coveredProjects = new HashSet<string>(
            evidence.SelectMany(item => item.CoversProjects ?? Array.Empty<string>()).Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var project in ownerProjects.Concat(consumerProjects).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!coveredProjects.Contains(project))
            {
                AddError(
                    issues,
                    "ARC226",
                    $"Capability '{capabilityId}' has no evidence covering project '{project}'.",
                    project,
                    capabilityId);
            }
        }
    }

    private static bool IsInsideAnyProject(
        string changedPath,
        IEnumerable<string> projectPaths,
        IReadOnlyDictionary<string, DiscoveredProject> projectLookup)
    {
        foreach (var projectPath in projectPaths)
        {
            if (projectLookup.TryGetValue(NormalizePath(projectPath), out var project)
                && IsPathInside(changedPath, project.DirectoryPath))
                return true;
        }

        return false;
    }

    private static bool EvidenceContainsPath(
        string changedPath,
        IEnumerable<RepositoryArchitectureEvidence> evidence)
    {
        foreach (var item in evidence)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
                continue;

            var evidencePath = NormalizePath(item.Path);
            if (IsPathInside(changedPath, evidencePath))
                return true;
        }

        return false;
    }
}
