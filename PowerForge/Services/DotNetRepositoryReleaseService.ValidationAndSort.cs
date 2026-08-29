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
        string? configuration = null)
    {
        if (projects is null)
            throw new ArgumentNullException(nameof(projects));
        if (projects.Count <= 1)
            return projects.ToArray();

        var byPackageId = new Dictionary<string, DotNetRepositoryProjectResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var packageId = GetEffectivePackageId(project);
            if (byPackageId.ContainsKey(packageId))
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order because package id '{packageId}' is produced by more than one selected project.");
            byPackageId.Add(packageId, project);
        }

        var edges = new List<PublishDependencyEdge>();
        foreach (var entry in byPackageId)
        {
            var selectedDependencies = usePlannedProjectGraph
                ? ReadPlannedProjectDependencies(entry.Value, byPackageId, configuration)
                : ReadSelectedPackageDependencies(entry.Value, entry.Key, byPackageId);
            foreach (var dependency in selectedDependencies)
                edges.Add(new PublishDependencyEdge(entry.Key, dependency.PackageId, dependency.Framework));
        }

        if (TryOrderProjects(byPackageId, edges, out var ordered, out var cycle))
            return ordered;

        var frameworkGroups = edges
            .Select(edge => edge.Framework)
            .Where(framework => !string.Equals(framework, PublishDependency.AllFrameworks, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var framework in frameworkGroups)
        {
            var frameworkEdges = edges.Where(edge =>
                string.Equals(edge.Framework, PublishDependency.AllFrameworks, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edge.Framework, framework, StringComparison.OrdinalIgnoreCase));
            if (!TryOrderProjects(byPackageId, frameworkEdges, out _, out var frameworkCycle))
                ThrowDependencyCycle(frameworkCycle);
        }

        if (frameworkGroups.Length == 0)
            ThrowDependencyCycle(cycle);

        var preferredEdges = edges.Where(edge =>
            string.Equals(edge.Framework, PublishDependency.AllFrameworks, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(edge.Framework, frameworkGroups[0], StringComparison.OrdinalIgnoreCase));
        if (TryOrderProjects(byPackageId, preferredEdges, out ordered, out _))
            return ordered;

        ThrowDependencyCycle(cycle);
        return Array.Empty<DotNetRepositoryProjectResult>();
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
                var nuspecEntries = archive.Entries
                    .Where(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (nuspecEntries.Length != 1)
                    throw new InvalidOperationException($"Package artifact '{packagePath}' must contain exactly one .nuspec file; found {nuspecEntries.Length}.");

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
                    var framework = dependency.Ancestors().FirstOrDefault(element =>
                        element.Name.LocalName.Equals("group", StringComparison.OrdinalIgnoreCase))
                        ?.Attribute("targetFramework")?.Value?.Trim();
                    var effectiveFramework = string.IsNullOrWhiteSpace(framework) ? PublishDependency.AllFrameworks : framework!;
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
        string? configuration)
    {
        if (string.IsNullOrWhiteSpace(project.CsprojPath) || !File.Exists(project.CsprojPath))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because its project file does not exist.");

        var selectedProjectPaths = selectedPackages.ToDictionary(
            entry => Path.GetFullPath(entry.Value.CsprojPath),
            entry => entry.Key,
            StringComparer.OrdinalIgnoreCase);
        var documents = LoadPlannedDocuments(project.CsprojPath);
        var frameworks = ReadPlannedTargetFrameworks(documents, configuration);
        var evaluations = frameworks.Length == 0 ? new string?[] { null } : frameworks.Cast<string?>().ToArray();
        var dependencies = new Dictionary<string, PublishDependency>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetFramework in evaluations)
        {
            var properties = ReadPlannedProperties(documents, configuration, targetFramework);
            foreach (var reference in documents.SelectMany(document => document.Descendants()).Where(element =>
                         element.Name.LocalName.Equals("ProjectReference", StringComparison.OrdinalIgnoreCase) &&
                         IsPlannedItemActive(element, configuration, targetFramework) &&
                         !IsPrivateReference(element)))
            {
                var include = reference.Attribute("Include")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(include))
                    continue;
                include = ExpandPlannedProperties(include!, properties);
                if (include.IndexOf("$(", StringComparison.Ordinal) >= 0)
                    continue;
                var fullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.CsprojPath)!, include));
                if (selectedProjectPaths.TryGetValue(fullPath, out var packageId))
                    AddPlannedDependency(dependencies, packageId, targetFramework);
            }

            foreach (var reference in documents.SelectMany(document => document.Descendants()).Where(element =>
                         element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase) &&
                         IsPlannedItemActive(element, configuration, targetFramework) &&
                         !IsPrivateReference(element)))
            {
                var packageId = (reference.Attribute("Include") ?? reference.Attribute("Update"))?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(packageId))
                    continue;
                var effectivePackageId = ExpandPlannedProperties(packageId!, properties);
                if (effectivePackageId.IndexOf("$(", StringComparison.Ordinal) >= 0)
                    continue;
                if (!selectedPackages.TryGetValue(effectivePackageId, out var selectedPackage))
                    continue;
                var versionRange = ReadItemMetadata(reference, "VersionOverride") ?? ReadItemMetadata(reference, "Version");
                if (!string.IsNullOrWhiteSpace(versionRange))
                    versionRange = ExpandPlannedProperties(versionRange!, properties);
                if (DependencyTargetsSelectedVersion(versionRange, selectedPackage, GetEffectivePackageId(project)))
                    AddPlannedDependency(dependencies, effectivePackageId, targetFramework);
            }
        }

        return dependencies.Values.ToArray();
    }

    private static void AddPlannedDependency(
        IDictionary<string, PublishDependency> dependencies,
        string packageId,
        string? targetFramework)
    {
        var framework = string.IsNullOrWhiteSpace(targetFramework) ? PublishDependency.AllFrameworks : targetFramework!;
        dependencies[packageId + "\n" + framework] = new PublishDependency(packageId, framework);
    }

    private static string[] ReadPlannedTargetFrameworks(IEnumerable<XDocument> documents, string? configuration)
        => documents.SelectMany(document => document.Descendants())
            .Where(element =>
                (element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase)) &&
                ConditionMatches(element.Parent?.Attribute("Condition")?.Value, configuration, null) &&
                ConditionMatches(element.Attribute("Condition")?.Value, configuration, null))
            .SelectMany(element => element.Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0 && value.IndexOf("$(", StringComparison.Ordinal) < 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsPlannedItemActive(XElement element, string? configuration, string? targetFramework)
        => ConditionMatches(element.Parent?.Attribute("Condition")?.Value, configuration, targetFramework) &&
           ConditionMatches(element.Attribute("Condition")?.Value, configuration, targetFramework);

    private static IReadOnlyDictionary<string, string> ReadPlannedProperties(
        IEnumerable<XDocument> documents,
        string? configuration,
        string? targetFramework)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration ?? string.Empty,
            ["TargetFramework"] = targetFramework ?? string.Empty
        };
        foreach (var group in documents.SelectMany(document => document.Descendants()).Where(element =>
                     element.Name.LocalName.Equals("PropertyGroup", StringComparison.OrdinalIgnoreCase) &&
                     ConditionMatches(element.Attribute("Condition")?.Value, configuration, targetFramework)))
        {
            foreach (var property in group.Elements().Where(element =>
                         ConditionMatches(element.Attribute("Condition")?.Value, configuration, targetFramework)))
            {
                properties[property.Name.LocalName] = ExpandPlannedProperties(property.Value.Trim(), properties);
            }
        }
        return properties;
    }

    private static string ExpandPlannedProperties(string value, IReadOnlyDictionary<string, string> properties)
        => Regex.Replace(value, @"\$\((?<name>[^)]+)\)", match =>
            properties.TryGetValue(match.Groups["name"].Value, out var replacement) ? replacement : match.Value);

    private static IReadOnlyList<XDocument> LoadPlannedDocuments(string projectPath)
    {
        var documents = new List<XDocument>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(projectPath))!);
        var directoryBuildProps = FindNearestBuildFile(directory, "Directory.Build.props");
        var directoryBuildTargets = FindNearestBuildFile(directory, "Directory.Build.targets");
        if (directoryBuildProps is not null)
            LoadPlannedDocument(directoryBuildProps, documents, visited);
        LoadPlannedDocument(Path.GetFullPath(projectPath), documents, visited);
        if (directoryBuildTargets is not null)
            LoadPlannedDocument(directoryBuildTargets, documents, visited);
        return documents;
    }

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

    private static void LoadPlannedDocument(
        string path,
        ICollection<XDocument> documents,
        ISet<string> visited)
    {
        var fullPath = Path.GetFullPath(path);
        if (!visited.Add(fullPath) || !File.Exists(fullPath))
            return;
        var document = XDocument.Load(fullPath, LoadOptions.PreserveWhitespace);
        documents.Add(document);
        var directory = Path.GetDirectoryName(fullPath)!;
        foreach (var import in document.Descendants().Where(element =>
                     element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase)))
        {
            var importedPath = import.Attribute("Project")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(importedPath) || importedPath!.IndexOf("$(", StringComparison.Ordinal) >= 0)
                continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, importedPath!));
            if (File.Exists(candidate))
                LoadPlannedDocument(candidate, documents, visited);
        }
    }

    private static bool ConditionMatches(string? condition, string? configuration, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var expanded = ExpandPlannedProperties(condition!, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Configuration"] = configuration ?? string.Empty,
            ["TargetFramework"] = targetFramework ?? string.Empty
        });
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0)
            return true;

        var orBranches = Regex.Split(expanded, @"\s+[Oo][Rr]\s+");
        return orBranches.Any(branch => Regex.Split(branch, @"\s+[Aa][Nn][Dd]\s+").All(EvaluateSimpleCondition));
    }

    private static bool EvaluateSimpleCondition(string condition)
    {
        var match = Regex.Match(
            condition.Trim(),
            "^\\s*\\(?\\s*['\\\"](?<left>[^'\\\"]*)['\\\"]\\s*(?<operator>==|!=)\\s*['\\\"](?<right>[^'\\\"]*)['\\\"]\\s*\\)?\\s*$");
        if (!match.Success)
            return true;
        var equal = string.Equals(match.Groups["left"].Value, match.Groups["right"].Value, StringComparison.OrdinalIgnoreCase);
        return match.Groups["operator"].Value == "==" ? equal : !equal;
    }

    private static bool IsPrivateReference(XElement element)
    {
        var privateAssets = ReadItemMetadata(element, "PrivateAssets");
        return (privateAssets ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(value => string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadItemMetadata(XElement element, string name)
        => element.Attribute(name)?.Value?.Trim() ??
           element.Elements().FirstOrDefault(child => child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

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
            var nuspec = archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
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

    private sealed class PublishDependency
    {
        internal const string AllFrameworks = "*";

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

}
