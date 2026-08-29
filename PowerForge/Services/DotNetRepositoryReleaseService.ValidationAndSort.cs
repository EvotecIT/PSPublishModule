using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    // nuget.org documents an approximate 250 MB upload limit.
    internal const long NuGetOrgPackageSizeLimitBytes = 250L * 1024L * 1024L;

    private static HashSet<string> BuildNameSet(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (items is null) return set;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            set.Add(item.Trim());
        }
        return set;
    }

    private static IReadOnlyList<string> BuildExcludeDirectories(IEnumerable<string>? items)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in DefaultExcludeDirectories)
            set.Add(dir);

        if (items is not null)
        {
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                set.Add(item.Trim());
            }
        }

        return set.ToArray();
    }

    internal (bool Success, string? ErrorMessage) ValidatePublishPreflight(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec)
    {
        var versionSources = GetPublishPreflightVersionSources(spec);
        var enforceNuGetOrgPackageSize = !spec.WhatIf &&
                                            IsNuGetOrgPublishSource(spec.PublishSource, spec.RootPath);

        foreach (var project in projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ErrorMessage))
                return (false, $"Publish preflight failed: {project.ProjectName} has errors: {project.ErrorMessage}");

            if (string.IsNullOrWhiteSpace(project.NewVersion))
                return (false, $"Publish preflight failed: {project.ProjectName} has no resolved version.");

            if (project.Packages.Count == 0)
                return (false, $"Publish preflight failed: {project.ProjectName} has no packages to publish.");

            foreach (var pkg in project.Packages.Concat(project.SymbolPackages))
            {
                if (!spec.WhatIf && !File.Exists(pkg))
                    return (false, $"Publish preflight failed: package not found: {pkg}");

                var packageLength = enforceNuGetOrgPackageSize ? new FileInfo(pkg).Length : 0;
                if (packageLength > NuGetOrgPackageSizeLimitBytes)
                {
                    return (false,
                        $"Publish preflight failed: {Path.GetFileName(pkg)} is {FormatBytes(packageLength)}, " +
                        $"which exceeds the nuget.org package limit of about {FormatBytes(NuGetOrgPackageSizeLimitBytes)}.");
                }
            }

            if (!PackageVersionUtility.TryNormalizeExact(project.NewVersion, out var target))
                continue;

            var latest = _resolver.ResolveLatestPackageVersion(
                packageId: string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId,
                sources: versionSources,
                credential: spec.VersionSourceCredential,
                credentialsBySource: spec.VersionSourceCredentials,
                includePrerelease: spec.IncludePrerelease || PackageVersionUtility.GetPrereleaseVersion(target).Length > 0);

            if (latest is not null && PackageVersionUtility.Compare(latest, target) >= 0)
            {
                if (!spec.SkipDuplicate)
                    return (false, $"Publish preflight failed: {project.ProjectName} version {target} already exists (latest {latest}). Use -SkipDuplicate to allow.");
            }
        }

        return (true, null);
    }

    private static bool IsNuGetOrgPublishSource(string? source, string? searchRoot)
    {
        if (string.IsNullOrWhiteSpace(source))
            return true;

        var trimmed = source!.Trim();
        if (IsNuGetOrgEndpoint(trimmed))
            return true;

        if (!string.IsNullOrWhiteSpace(searchRoot) &&
            TryResolveNamedPublishSource(trimmed, searchRoot!, out var configuredSource))
        {
            return IsNuGetOrgEndpoint(configuredSource);
        }

        // Keep the conventional key safe even when no NuGet.config is available locally.
        return string.Equals(trimmed, "nuget.org", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNuGetOrgEndpoint(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) &&
               (string.Equals(uri.Host, "nuget.org", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".nuget.org", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string>? GetPublishPreflightVersionSources(DotNetRepositoryReleaseSpec spec)
    {
        var configured = (spec.VersionSources ?? Array.Empty<string>())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(source => source.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (configured.Length > 0)
            return configured;

        if (!string.IsNullOrWhiteSpace(spec.PublishSource))
            return new[] { spec.PublishSource!.Trim() };

        return null;
    }

    internal IReadOnlyList<DotNetRepositoryProjectResult> SortProjectsForPublish(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        bool usePlannedProjectGraph = false,
        string? configuration = null,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath = null)
        => CreatePublishPlan(projects, usePlannedProjectGraph, configuration, plannedProjectContentsByPath).OrderedProjects;

    private static PublishPlan CreatePublishPlan(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        bool usePlannedProjectGraph,
        string? configuration,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        if (projects is null)
            throw new ArgumentNullException(nameof(projects));
        if (projects.Count <= 1)
        {
            var dependencies = projects.Count == 0
                ? new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [GetEffectivePackageId(projects[0])] = Array.Empty<string>()
                };
            return new PublishPlan(projects.ToArray(), dependencies);
        }

        var byPackageId = new Dictionary<string, DotNetRepositoryProjectResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var packageId = GetEffectivePackageId(project);
            if (byPackageId.ContainsKey(packageId))
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order because package id '{packageId}' is produced by more than one selected project.");
            byPackageId.Add(packageId, project);
        }

        var edges = new List<PublishDependencyEdge>();
        var dependenciesByPackageId = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => (ISet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in byPackageId)
        {
            var selectedDependencies = usePlannedProjectGraph
                ? ReadPlannedProjectDependencies(entry.Value, byPackageId, configuration, plannedProjectContentsByPath)
                : ReadSelectedPackageDependencies(entry.Value, entry.Key, byPackageId);
            foreach (var dependency in selectedDependencies)
            {
                edges.Add(new PublishDependencyEdge(entry.Key, dependency.PackageId, dependency.Framework));
                dependenciesByPackageId[entry.Key].Add(dependency.PackageId);
            }
        }

        if (TryOrderProjects(byPackageId, edges, out var ordered, out var cycle))
        {
            return new PublishPlan(
                ordered,
                dependenciesByPackageId.ToDictionary(
                    entry => entry.Key,
                    entry => (IReadOnlyCollection<string>)entry.Value.ToArray(),
                    StringComparer.OrdinalIgnoreCase));
        }

        ThrowDependencyCycle(cycle);
        return new PublishPlan(
            Array.Empty<DotNetRepositoryProjectResult>(),
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool TryOrderProjects(
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> byPackageId,
        IEnumerable<PublishDependencyEdge> selectedEdges,
        out IReadOnlyList<DotNetRepositoryProjectResult> orderedProjects,
        out IReadOnlyList<string> cycle)
    {
        var dependencies = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var dependents = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var edge in selectedEdges)
        {
            dependencies[edge.Consumer].Add(edge.Dependency);
            dependents[edge.Dependency].Add(edge.Consumer);
        }

        var inDegree = dependencies.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Count,
            StringComparer.OrdinalIgnoreCase);
        var ready = new SortedSet<string>(
            inDegree.Where(entry => entry.Value == 0).Select(entry => entry.Key),
            StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DotNetRepositoryProjectResult>();

        while (ready.Count > 0)
        {
            var packageId = ready.Min!;
            ready.Remove(packageId);
            ordered.Add(byPackageId[packageId]);

            foreach (var child in dependents[packageId])
            {
                inDegree[child]--;
                if (inDegree[child] == 0)
                    ready.Add(child);
            }
        }

        if (ordered.Count != byPackageId.Count)
        {
            cycle = inDegree
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            orderedProjects = Array.Empty<DotNetRepositoryProjectResult>();
            return false;
        }

        cycle = Array.Empty<string>();
        orderedProjects = ordered;
        return true;
    }

    private static void ThrowDependencyCycle(IEnumerable<string> cycle)
        => throw new InvalidOperationException(
            $"NuGet package dependency cycle detected among selected projects: {string.Join(", ", cycle)}. Publishing stopped before any package was pushed.");

    private static string GetEffectivePackageId(DotNetRepositoryProjectResult project)
    {
        var packageId = string.IsNullOrWhiteSpace(project.PackageId)
            ? project.ProjectName
            : project.PackageId;
        if (string.IsNullOrWhiteSpace(packageId))
            throw new InvalidOperationException("Cannot determine a safe NuGet publish order because a selected project has no package id or project name.");

        return packageId.Trim();
    }

    private static IReadOnlyCollection<PublishDependency> ReadSelectedPackageDependencies(
        DotNetRepositoryProjectResult project,
        string expectedPackageId,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages)
    {
        var packagePaths = project.Packages
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagePaths.Length == 0)
            throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{expectedPackageId}' because it has no primary package artifact.");

        var dependencies = new Dictionary<string, PublishDependency>(StringComparer.OrdinalIgnoreCase);
        foreach (var packagePath in packagePaths)
        {
            if (!File.Exists(packagePath))
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{expectedPackageId}' because package artifact '{packagePath}' does not exist.");

            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                var nuspecEntries = GetRootNuspecEntries(archive);
                if (nuspecEntries.Length != 1)
                    throw new InvalidOperationException($"Package artifact '{packagePath}' must contain exactly one .nuspec file at the archive root; found {nuspecEntries.Length}.");

                using var stream = nuspecEntries[0].Open();
                var document = XDocument.Load(stream);
                var metadata = document.Descendants().FirstOrDefault(element =>
                    element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
                var declaredPackageId = metadata?.Elements().FirstOrDefault(element =>
                    element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                if (!string.Equals(declaredPackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Package artifact '{packagePath}' declares id '{declaredPackageId ?? "<missing>"}', expected '{expectedPackageId}'.");

                foreach (var dependency in metadata!.Descendants().Where(element =>
                             element.Name.LocalName.Equals("dependency", StringComparison.OrdinalIgnoreCase)))
                {
                    var dependencyId = dependency.Attribute("id")?.Value?.Trim();
                    if (dependencyId is null || dependencyId.Length == 0 || !selectedPackages.TryGetValue(dependencyId, out var selectedPackage))
                        continue;
                    var versionRange = dependency.Attribute("version")?.Value?.Trim();
                    if (!DependencyTargetsSelectedVersion(versionRange, selectedPackage, expectedPackageId))
                        continue;
                    var group = dependency.Ancestors().FirstOrDefault(element =>
                        element.Name.LocalName.Equals("group", StringComparison.OrdinalIgnoreCase));
                    var framework = group?.Attribute("targetFramework")?.Value?.Trim();
                    var effectiveFramework = group is null
                        ? PublishDependency.AllFrameworks
                        : string.IsNullOrWhiteSpace(framework)
                            ? PublishDependency.FallbackFramework
                            : framework!;
                    dependencies[dependencyId + "\n" + effectiveFramework] = new PublishDependency(dependencyId, effectiveFramework);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Cannot determine a safe NuGet publish order for '{expectedPackageId}' from package artifact '{packagePath}': {ex.Message}",
                    ex);
            }
        }

        return dependencies.Values.ToArray();
    }

    private static IReadOnlyCollection<PublishDependency> ReadPlannedProjectDependencies(
        DotNetRepositoryProjectResult project,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        string? configuration,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        if (string.IsNullOrWhiteSpace(project.CsprojPath) || !File.Exists(project.CsprojPath))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because its project file does not exist.");

        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(project.CsprojPath))!;
        var pathComparer = FrameworkCompatibility.GetPathStringComparison(projectDirectory) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var selectedProjectPaths = selectedPackages.ToDictionary(
            entry => Path.GetFullPath(entry.Value.CsprojPath),
            entry => entry.Key,
            pathComparer);
        var outerEvaluation = EvaluatePlannedProject(project.CsprojPath, configuration, targetFramework: null, plannedProjectContentsByPath);
        var frameworks = ReadPlannedTargetFrameworks(outerEvaluation.Properties);
        var evaluations = frameworks.Length == 0 ? new string?[] { null } : frameworks.Cast<string?>().ToArray();
        var dependencies = new Dictionary<string, PublishDependency>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetFramework in evaluations)
        {
            var evaluation = EvaluatePlannedProject(project.CsprojPath, configuration, targetFramework, plannedProjectContentsByPath);
            if (evaluation.Properties.TryGetValue("NuspecFile", out var nuspecFile) && !string.IsNullOrWhiteSpace(nuspecFile))
            {
                ReadPlannedNuspecDependencies(evaluation, project, selectedPackages, dependencies, plannedProjectContentsByPath);
                continue;
            }
            var suppressesDependencies =
                (evaluation.Properties.TryGetValue("SuppressDependenciesWhenPacking", out var suppressDependencies) && string.Equals(suppressDependencies, "true", StringComparison.OrdinalIgnoreCase)) ||
                (evaluation.Properties.TryGetValue("PackAsTool", out var packAsTool) && string.Equals(packAsTool, "true", StringComparison.OrdinalIgnoreCase));
            if (suppressesDependencies)
            {
                continue;
            }

            foreach (var item in evaluation.Items.Where(item => item.ItemType.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var include in SplitPlannedItems(item.Include))
                    EnsurePlannedReferenceIsResolved(include, project);
            }
            foreach (var selectedProject in selectedProjectPaths)
            {
                var reference = ResolvePlannedItemForCandidate(
                    evaluation.Items,
                    "ProjectReference",
                    selectedProject.Key,
                    static (item, pattern, candidate) => PlannedProjectReferenceMatches(item.BaseDirectory, pattern, candidate));
                if (reference is not null && IsPackedProjectReference(reference))
                    AddPlannedDependency(dependencies, selectedProject.Value, targetFramework);
            }

            var centralVersionsEnabled = evaluation.Properties.TryGetValue("ManagePackageVersionsCentrally", out var centralSetting) &&
                                         string.Equals(centralSetting, "true", StringComparison.OrdinalIgnoreCase);
            var centralVersions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (centralVersionsEnabled)
            {
                foreach (var packageId in selectedPackages.Keys)
                {
                    var versionItem = ResolvePlannedItemForCandidate(
                        evaluation.Items,
                        "PackageVersion",
                        packageId,
                        static (_, pattern, candidate) => PlannedItemSpecMatches(pattern, candidate));
                    if (versionItem is not null)
                        centralVersions[packageId] = versionItem.GetMetadata("Version");
                }
            }
            foreach (var item in evaluation.Items.Where(item => item.ItemType.Equals("PackageReference", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var include in SplitPlannedItems(item.Include))
                    EnsurePlannedReferenceIsResolved(include, project);
            }
            foreach (var selectedPackage in selectedPackages)
            {
                var reference = ResolvePlannedItemForCandidate(
                    evaluation.Items,
                    "PackageReference",
                    selectedPackage.Key,
                    static (_, pattern, candidate) => PlannedItemSpecMatches(pattern, candidate));
                if (reference is null || IsPrivateReference(reference))
                    continue;
                var versionRange = reference.GetMetadata("VersionOverride") ?? reference.GetMetadata("Version");
                if (string.IsNullOrWhiteSpace(versionRange) && centralVersions.TryGetValue(selectedPackage.Key, out var centralVersion))
                    versionRange = centralVersion;
                if (DependencyTargetsSelectedVersion(versionRange, selectedPackage.Value, GetEffectivePackageId(project)))
                    AddPlannedDependency(dependencies, selectedPackage.Key, targetFramework);
            }
        }

        return dependencies.Values.ToArray();
    }

    private static void ReadPlannedNuspecDependencies(
        PlannedEvaluation evaluation,
        DotNetRepositoryProjectResult project,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        IDictionary<string, PublishDependency> dependencies,
        IReadOnlyDictionary<string, string>? plannedProjectContentsByPath)
    {
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(project.CsprojPath))!;
        var configuredPath = ExpandPlannedProperties(evaluation.Properties["NuspecFile"], evaluation.Properties);
        if (configuredPath.IndexOf("$(", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because NuspecFile '{configuredPath}' is unresolved.");
        var nuspecPath = ResolvePlannedPath(projectDirectory, configuredPath);
        if (!File.Exists(nuspecPath))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because custom nuspec '{nuspecPath}' does not exist.");

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in evaluation.Properties)
            tokens[property.Key] = property.Value;
        tokens["id"] = GetEffectivePackageId(project);
        tokens["version"] = GetSelectedPackageVersion(project);
        if (evaluation.Properties.TryGetValue("NuspecProperties", out var nuspecProperties))
        {
            foreach (var entry in SplitPlannedItems(ExpandPlannedProperties(nuspecProperties, evaluation.Properties)))
            {
                var separator = entry.IndexOf('=');
                if (separator <= 0)
                    throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because NuspecProperties entry '{entry}' is invalid.");
                tokens[entry.Substring(0, separator).Trim()] = entry.Substring(separator + 1).Trim();
            }
        }

        string ExpandToken(string value)
        {
            var expanded = Regex.Replace(value, @"\$(?<name>[^$]+)\$", match =>
                tokens.TryGetValue(match.Groups["name"].Value, out var replacement) ? replacement : match.Value);
            if (expanded.IndexOf('$') >= 0)
                throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because custom nuspec value '{value}' contains an unresolved token.");
            return expanded;
        }

        var nuspec = plannedProjectContentsByPath is not null && plannedProjectContentsByPath.TryGetValue(nuspecPath, out var plannedNuspecContent)
            ? XDocument.Parse(plannedNuspecContent, LoadOptions.PreserveWhitespace)
            : XDocument.Load(nuspecPath);
        var metadata = nuspec.Descendants().FirstOrDefault(element =>
            element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
        if (metadata is null)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because custom nuspec '{nuspecPath}' has no metadata element.");

        foreach (var dependency in metadata.Descendants().Where(element => element.Name.LocalName.Equals("dependency", StringComparison.OrdinalIgnoreCase)))
        {
            var dependencyId = ExpandToken(dependency.Attribute("id")?.Value?.Trim() ?? string.Empty);
            if (dependencyId.Length == 0 || !selectedPackages.TryGetValue(dependencyId, out var selectedPackage))
                continue;
            var versionRange = ExpandToken(dependency.Attribute("version")?.Value?.Trim() ?? string.Empty);
            if (!DependencyTargetsSelectedVersion(versionRange, selectedPackage, GetEffectivePackageId(project)))
                continue;
            var group = dependency.Ancestors().FirstOrDefault(element => element.Name.LocalName.Equals("group", StringComparison.OrdinalIgnoreCase));
            var framework = group?.Attribute("targetFramework")?.Value?.Trim();
            AddPlannedDependency(
                dependencies,
                dependencyId,
                group is null ? PublishDependency.AllFrameworks : string.IsNullOrWhiteSpace(framework) ? PublishDependency.FallbackFramework : framework);
        }
    }

    private static void EnsurePlannedReferenceIsResolved(string value, DotNetRepositoryProjectResult project)
    {
        if (value.IndexOf("$(", StringComparison.Ordinal) >= 0 || value.IndexOf("@(", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because reference '{value}' contains an unresolved MSBuild expression.");
    }

    private static bool PlannedProjectReferenceMatches(string baseDirectory, string include, string selectedProjectPath)
    {
        var normalizedInclude = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var candidatePattern = Path.GetFullPath(Path.Combine(baseDirectory, normalizedInclude));
        var candidate = Path.GetFullPath(selectedProjectPath);
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(candidate);
        if (candidatePattern.IndexOfAny(new[] { '*', '?' }) < 0)
            return string.Equals(candidatePattern, candidate, comparison);
        var normalizedPattern = NormalizePlannedItemSpec(candidatePattern);
        var normalizedCandidate = NormalizePlannedItemSpec(candidate);
        var regex = "^" + Regex.Escape(normalizedPattern)
            .Replace(@"\*\*/", "(?:.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]") + "$";
        var options = RegexOptions.CultureInvariant |
                      (comparison == StringComparison.OrdinalIgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.IsMatch(normalizedCandidate, regex, options);
    }

    private static PlannedItem? ResolvePlannedItemForCandidate(
        IEnumerable<PlannedItem> source,
        string itemType,
        string candidate,
        Func<PlannedItem, string, string, bool> matches)
    {
        PlannedItem? result = null;
        foreach (var item in source.Where(item => item.ItemType.Equals(itemType, StringComparison.OrdinalIgnoreCase)))
        {
            if (SplitPlannedItems(item.Remove).Any(pattern => matches(item, pattern, candidate)))
                result = null;
            if (result is not null && SplitPlannedItems(item.Update).Any(pattern => matches(item, pattern, candidate)))
                result = result.WithMetadata(item.Metadata, item.RemoveMetadata, item.KeepMetadata);
            if (SplitPlannedItems(item.Include).Any(pattern => matches(item, pattern, candidate)) &&
                !SplitPlannedItems(item.Exclude).Any(pattern => matches(item, pattern, candidate)))
            {
                result = item.WithInclude(candidate);
            }
        }
        return result;
    }

    private static void AddPlannedDependency(
        IDictionary<string, PublishDependency> dependencies,
        string packageId,
        string? targetFramework)
    {
        var framework = string.IsNullOrWhiteSpace(targetFramework) ? PublishDependency.AllFrameworks : targetFramework!;
        dependencies[packageId + "\n" + framework] = new PublishDependency(packageId, framework);
    }

    private static string[] ReadPlannedTargetFrameworks(IReadOnlyDictionary<string, string> properties)
    {
        var value = properties.TryGetValue("TargetFrameworks", out var frameworks) && !string.IsNullOrWhiteSpace(frameworks)
            ? frameworks
            : properties.TryGetValue("TargetFramework", out var framework) ? framework : string.Empty;
        var expanded = ExpandPlannedProperties(value, properties);
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("@(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because target frameworks '{expanded}' contain an unsupported MSBuild expression.");
        }
        return expanded
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ExpandPlannedProperties(string value, IReadOnlyDictionary<string, string> properties)
        => Regex.Replace(value, @"\$\((?<name>[A-Za-z_][A-Za-z0-9_.-]*)\)", match =>
            properties.TryGetValue(match.Groups["name"].Value, out var replacement) ? replacement : string.Empty);

    private static string? FindNearestBuildFile(DirectoryInfo directory, string fileName)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private static bool IsPrivateReference(PlannedItem element)
    {
        var privateAssets = element.GetMetadata("PrivateAssets");
        return (privateAssets ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPackedProjectReference(PlannedItem reference)
        => !IsPrivateReference(reference) &&
           !string.Equals(reference.GetMetadata("BuildReference"), "false", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(reference.GetMetadata("TreatAsPackageReference"), "false", StringComparison.OrdinalIgnoreCase);

    private static bool DependencyTargetsSelectedVersion(
        string? versionRange,
        DotNetRepositoryProjectResult selectedPackage,
        string consumerPackageId)
    {
        if (string.IsNullOrWhiteSpace(versionRange))
            return true;
        // Planning is deliberately process-free. If an MSBuild expression cannot be
        // resolved from the project and its safe local imports, preserve the edge so
        // WhatIf remains conservative rather than risking a dependency-first violation.
        if (versionRange!.IndexOf("$(", StringComparison.Ordinal) >= 0)
            return true;
        var selectedVersion = GetSelectedPackageVersion(selectedPackage);
        if (!NuGetVersion.TryParse(selectedVersion, out var version) || !VersionRange.TryParse(versionRange!, out var range))
            throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{consumerPackageId}' because dependency '{GetEffectivePackageId(selectedPackage)}' has invalid version range '{versionRange}' or selected version '{selectedVersion}'.");
        return range.Satisfies(version);
    }

    private static string GetSelectedPackageVersion(DotNetRepositoryProjectResult project)
    {
        if (!string.IsNullOrWhiteSpace(project.NewVersion))
            return project.NewVersion!;
        foreach (var packagePath in project.Packages.Where(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
        {
            if (!File.Exists(packagePath))
                continue;
            using var archive = ZipFile.OpenRead(packagePath);
            var nuspec = GetRootNuspecEntries(archive).SingleOrDefault();
            if (nuspec is null)
                continue;
            using var stream = nuspec.Open();
            var metadata = XDocument.Load(stream).Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
            var version = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(version))
                return version!;
        }
        throw new InvalidOperationException($"Cannot determine the selected version for package '{GetEffectivePackageId(project)}'.");
    }

    private static ZipArchiveEntry[] GetRootNuspecEntries(ZipArchive archive)
        => archive.Entries.Where(entry =>
        {
            var name = entry.FullName.Replace('\\', '/');
            return name.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && name.IndexOf('/') < 0;
        }).ToArray();

    private sealed class PublishDependency
    {
        internal const string AllFrameworks = "*";
        internal const string FallbackFramework = "<fallback>";

        internal PublishDependency(string packageId, string framework)
        {
            PackageId = packageId;
            Framework = framework;
        }

        internal string PackageId { get; }
        internal string Framework { get; }
    }

    private sealed class PublishDependencyEdge
    {
        internal PublishDependencyEdge(string consumer, string dependency, string framework)
        {
            Consumer = consumer;
            Dependency = dependency;
            Framework = framework;
        }

        internal string Consumer { get; }
        internal string Dependency { get; }
        internal string Framework { get; }
    }

    private sealed class PublishPlan
    {
        internal PublishPlan(
            IReadOnlyList<DotNetRepositoryProjectResult> orderedProjects,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependenciesByPackageId)
        {
            OrderedProjects = orderedProjects;
            DependenciesByPackageId = dependenciesByPackageId;
        }

        internal IReadOnlyList<DotNetRepositoryProjectResult> OrderedProjects { get; }
        internal IReadOnlyDictionary<string, IReadOnlyCollection<string>> DependenciesByPackageId { get; }
    }

}
