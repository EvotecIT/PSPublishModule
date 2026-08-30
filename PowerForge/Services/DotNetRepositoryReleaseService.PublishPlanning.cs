using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    internal IReadOnlyList<DotNetRepositoryProjectResult> SortProjectsForPublish(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        bool usePlannedProjectGraph = false,
        string? configuration = null,
        DotNetRepositoryPackStrategy packStrategy = DotNetRepositoryPackStrategy.PerProject,
        bool includeSymbols = false,
        string? packageOutputPath = null)
        => CreatePublishPlan(projects, usePlannedProjectGraph, configuration, packStrategy, includeSymbols, packageOutputPath).OrderedProjects;

    private PublishPlan CreatePublishPlan(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        bool usePlannedProjectGraph,
        string? configuration,
        DotNetRepositoryPackStrategy packStrategy,
        bool includeSymbols,
        string? packageOutputPath)
    {
        if (projects is null)
            throw new ArgumentNullException(nameof(projects));
        var byPackageId = new Dictionary<string, DotNetRepositoryProjectResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            var packageId = GetEffectivePackageId(project);
            if (byPackageId.ContainsKey(packageId))
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order because package id '{packageId}' is produced by more than one selected project.");
            byPackageId.Add(packageId, project);
        }

        var dependencies = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in byPackageId)
        {
            var selectedDependencies = usePlannedProjectGraph
                ? ReadPlannedProjectDependencies(entry.Value, byPackageId, configuration, packStrategy, includeSymbols, packageOutputPath)
                : ReadSelectedPackageDependencies(entry.Value, entry.Key, byPackageId);
            dependencies[entry.Key].UnionWith(selectedDependencies);
        }

        return new PublishPlan(OrderProjects(byPackageId, dependencies), dependencies);
    }

    private static IReadOnlyList<DotNetRepositoryProjectResult> OrderProjects(
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> byPackageId,
        IReadOnlyDictionary<string, SortedSet<string>> dependencies)
    {
        var dependents = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dependencies)
        {
            foreach (var dependency in entry.Value)
                dependents[dependency].Add(entry.Key);
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
            foreach (var dependent in dependents[packageId])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        if (ordered.Count != byPackageId.Count)
        {
            var cycle = inDegree.Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                $"NuGet package dependency cycle detected among selected projects: {string.Join(", ", cycle)}. Publishing stopped before any package was pushed.");
        }

        return ordered;
    }

    private static string GetEffectivePackageId(DotNetRepositoryProjectResult project)
    {
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        if (string.IsNullOrWhiteSpace(packageId))
            throw new InvalidOperationException("Cannot determine a safe NuGet publish order because a selected project has no package id or project name.");
        return packageId.Trim();
    }

    private static IReadOnlyCollection<string> ReadSelectedPackageDependencies(
        DotNetRepositoryProjectResult project,
        string expectedPackageId,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages)
    {
        var packagePaths = project.Packages
            .Where(path => !string.IsNullOrWhiteSpace(path) && path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (packagePaths.Length == 0)
            throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{expectedPackageId}' because it has no primary package artifact.");

        var dependencies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packagePath in packagePaths)
        {
            if (!File.Exists(packagePath))
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{expectedPackageId}' because package artifact '{packagePath}' does not exist.");
            try
            {
                using var archive = ZipFile.OpenRead(packagePath);
                var nuspecEntries = archive.Entries.Where(entry =>
                    entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
                    entry.FullName.IndexOf('/') < 0 &&
                    entry.FullName.IndexOf('\\') < 0).ToArray();
                if (nuspecEntries.Length != 1)
                    throw new InvalidOperationException($"Package artifact '{packagePath}' must contain exactly one .nuspec file; found {nuspecEntries.Length}.");
                using var stream = nuspecEntries[0].Open();
                var document = XDocument.Load(stream);
                var metadata = document.Descendants().FirstOrDefault(element => element.Name.LocalName.Equals("metadata", StringComparison.OrdinalIgnoreCase));
                var declaredPackageId = metadata?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("id", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
                if (!string.Equals(declaredPackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Package artifact '{packagePath}' declares id '{declaredPackageId ?? "<missing>"}', expected '{expectedPackageId}'.");

                foreach (var dependency in metadata!.Descendants().Where(element => element.Name.LocalName.Equals("dependency", StringComparison.OrdinalIgnoreCase)))
                {
                    var dependencyId = dependency.Attribute("id")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(dependencyId) || !selectedPackages.TryGetValue(dependencyId!, out var selectedPackage))
                        continue;
                    if (DependencyTargetsSelectedVersion(dependency.Attribute("version")?.Value, selectedPackage, expectedPackageId))
                        dependencies.Add(dependencyId!);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{expectedPackageId}' from package artifact '{packagePath}': {ex.Message}", ex);
            }
        }

        return dependencies;
    }

    private IReadOnlyCollection<string> ReadPlannedProjectDependencies(
        DotNetRepositoryProjectResult project,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        string? configuration,
        DotNetRepositoryPackStrategy packStrategy,
        bool includeSymbols,
        string? packageOutputPath)
    {
        if (string.IsNullOrWhiteSpace(project.CsprojPath) || !File.Exists(project.CsprojPath))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because its project file does not exist.");

        var pathComparer = FrameworkCompatibility.GetPathStringComparison(project.CsprojPath) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var selectedProjectPaths = selectedPackages.ToDictionary(
            entry => Path.GetFullPath(entry.Value.CsprojPath),
            entry => entry.Key,
            pathComparer);
        var dependencies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var useMsBuildTraversal = packStrategy == DotNetRepositoryPackStrategy.MSBuild && !string.IsNullOrWhiteSpace(packageOutputPath);
        var outer = EvaluatePublishPlanningItems(project, targetFramework: null, configuration, useMsBuildTraversal, includeSymbols, packageOutputPath);
        ValidatePlanningContract(project, outer);
        if (outer.DeclaredTargetFrameworks.Count == 0)
        {
            AddPlannedDependencies(project, outer, selectedPackages, selectedProjectPaths, dependencies);
        }
        else
        {
            foreach (var targetFramework in outer.DeclaredTargetFrameworks)
            {
                var evaluation = EvaluatePublishPlanningItems(project, targetFramework, configuration, useMsBuildTraversal, includeSymbols, packageOutputPath);
                ValidatePlanningContract(project, evaluation);
                AddPlannedDependencies(project, evaluation, selectedPackages, selectedProjectPaths, dependencies);
            }
        }

        return dependencies;
    }

    private static void ValidatePlanningContract(DotNetRepositoryProjectResult project, PublishPlanningEvaluation evaluation)
    {
        var packageId = evaluation.GetProperty("PackageId");
        if (string.IsNullOrWhiteSpace(packageId))
            packageId = evaluation.GetProperty("AssemblyName");
        if (!string.IsNullOrWhiteSpace(packageId) && !string.Equals(packageId!.Trim(), GetEffectivePackageId(project), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{project.ProjectName}' because MSBuild evaluates package id '{packageId}', while discovery selected '{GetEffectivePackageId(project)}'. Use a stable PackageId for publish planning.");
        if (!string.IsNullOrWhiteSpace(evaluation.GetProperty("NuspecFile")))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because custom NuspecFile dependency evaluation is intentionally unsupported. Build the packages and use artifact-based ordering.");
        if (string.Equals(evaluation.GetProperty("CentralPackageTransitivePinningEnabled"), "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' while CentralPackageTransitivePinningEnabled is active. Build the packages and use artifact-based ordering.");
    }

    private static void AddPlannedDependencies(
        DotNetRepositoryProjectResult consumer,
        PublishPlanningEvaluation evaluation,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        IReadOnlyDictionary<string, string> selectedProjectPaths,
        ISet<string> dependencies)
    {
        if (string.Equals(evaluation.GetProperty("SuppressDependenciesWhenPacking"), "true", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var reference in evaluation.ProjectReferences)
        {
            if (reference.IsProjectReferenceExcludedFromPackage())
                continue;
            var fullPath = reference.Get("FullPath");
            if (!string.IsNullOrWhiteSpace(fullPath) && selectedProjectPaths.TryGetValue(Path.GetFullPath(fullPath), out var packageId))
                dependencies.Add(packageId);
        }

        foreach (var reference in evaluation.PackageReferences)
        {
            if (reference.IsPackageReferenceExcludedFromPackage())
                continue;
            var packageId = reference.Get("Identity");
            if (string.IsNullOrWhiteSpace(packageId) || !selectedPackages.TryGetValue(packageId!, out var selectedPackage))
                continue;
            var versionRange = reference.Get("VersionOverride") ?? reference.Get("Version") ?? evaluation.GetCentralPackageVersion(packageId!);
            if (DependencyTargetsSelectedVersion(versionRange, selectedPackage, GetEffectivePackageId(consumer)))
                dependencies.Add(packageId!);
        }
    }

    private PublishPlanningEvaluation EvaluatePublishPlanningItems(
        DotNetRepositoryProjectResult project,
        string? targetFramework,
        string? configuration,
        bool useMsBuildTraversal,
        bool includeSymbols,
        string? packageOutputPath)
    {
        var projectPath = Path.GetFullPath(project.CsprojPath);
        var arguments = new List<string>
        {
            "msbuild", projectPath, "-nologo", "-verbosity:quiet",
            "-getProperty:TargetFrameworks", "-getProperty:TargetFramework",
            "-getProperty:PackageId", "-getProperty:AssemblyName", "-getProperty:NuspecFile",
            "-getProperty:CentralPackageTransitivePinningEnabled", "-getProperty:SuppressDependenciesWhenPacking",
            "-getItem:ProjectReference", "-getItem:PackageReference", "-getItem:PackageVersion",
            "-p:NoBuild=true",
            $"-p:Configuration={(string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration!.Trim())}"
        };
        if (useMsBuildTraversal)
            arguments.Add("-p:BuildProjectReferences=false");
        if (!string.IsNullOrWhiteSpace(packageOutputPath))
            arguments.Add($"-p:PackageOutputPath={EscapeMsBuildPropertyValue(Path.GetFullPath(packageOutputPath!))}");
        if (includeSymbols)
        {
            arguments.Add("-p:IncludeSymbols=true");
            arguments.Add("-p:SymbolPackageFormat=snupkg");
        }
        if (!string.IsNullOrWhiteSpace(targetFramework))
            arguments.Add($"-p:TargetFramework={targetFramework!.Trim()}");
        if (!string.IsNullOrWhiteSpace(project.NewVersion))
        {
            arguments.Add($"-p:Version={project.NewVersion!.Trim()}");
            arguments.Add($"-p:PackageVersion={project.NewVersion!.Trim()}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(startInfo);
#if NET472
        startInfo.Arguments = BuildWindowsArgumentString(arguments);
#else
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
#endif
        var exitCode = RunProcessWithHeartbeat(
            startInfo,
            _logger,
            elapsed => $"{project.ProjectName}: MSBuild publish planning still running ({FormatDuration(elapsed)} elapsed).",
            out var standardError,
            out var standardOutput,
            out _);
        LogProcessOutput(_logger, project.ProjectName, "dotnet msbuild publish planning", standardOutput, standardError);
        if (exitCode != 0)
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order because dotnet msbuild could not evaluate '{projectPath}' ({targetFramework ?? "outer build"}, exit {exitCode}): {SummarizeProcessFailureOutput(standardError, standardOutput)}");

        var jsonStart = standardOutput.IndexOf('{');
        var jsonEnd = standardOutput.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < jsonStart)
            throw new InvalidOperationException($"dotnet msbuild returned invalid planning metadata for '{projectPath}'.");
        try
        {
            using var document = JsonDocument.Parse(standardOutput.Substring(jsonStart, jsonEnd - jsonStart + 1));
            return PublishPlanningEvaluation.Parse(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException($"dotnet msbuild returned invalid planning metadata for '{projectPath}': {ex.Message}", ex);
        }
    }

    private static bool DependencyTargetsSelectedVersion(string? versionRange, DotNetRepositoryProjectResult selectedPackage, string consumerPackageId)
    {
        if (string.IsNullOrWhiteSpace(versionRange))
            return true;
        var selectedVersion = selectedPackage.NewVersion;
        if (string.IsNullOrWhiteSpace(selectedVersion))
            return true;
        if (!NuGetVersion.TryParse(selectedVersion!, out var version) || !VersionRange.TryParse(versionRange!, out var range))
            throw new InvalidOperationException($"Cannot determine a safe NuGet publish order for '{consumerPackageId}' because dependency '{GetEffectivePackageId(selectedPackage)}' has invalid version range '{versionRange}' or selected version '{selectedVersion}'.");
        return range.Satisfies(version);
    }

    private sealed class PublishPlan
    {
        internal PublishPlan(IReadOnlyList<DotNetRepositoryProjectResult> orderedProjects, IReadOnlyDictionary<string, SortedSet<string>> dependenciesByPackageId)
        {
            OrderedProjects = orderedProjects;
            DependenciesByPackageId = dependenciesByPackageId;
        }

        internal IReadOnlyList<DotNetRepositoryProjectResult> OrderedProjects { get; }
        internal IReadOnlyDictionary<string, SortedSet<string>> DependenciesByPackageId { get; }
    }

    private sealed class PublishPlanningEvaluation
    {
        private PublishPlanningEvaluation(
            Dictionary<string, string> properties,
            List<PublishPlanningItem> projectReferences,
            List<PublishPlanningItem> packageReferences,
            List<PublishPlanningItem> packageVersions)
        {
            Properties = properties;
            ProjectReferences = projectReferences;
            PackageReferences = packageReferences;
            PackageVersions = packageVersions;
        }

        private Dictionary<string, string> Properties { get; }
        internal IReadOnlyList<PublishPlanningItem> ProjectReferences { get; }
        internal IReadOnlyList<PublishPlanningItem> PackageReferences { get; }
        private IReadOnlyList<PublishPlanningItem> PackageVersions { get; }
        internal string? GetProperty(string name) => Properties.TryGetValue(name, out var value) ? value : null;
        internal IReadOnlyList<string> DeclaredTargetFrameworks => (GetProperty("TargetFrameworks") ?? string.Empty)
            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        internal string? GetCentralPackageVersion(string packageId)
        {
            var item = PackageVersions.FirstOrDefault(candidate => string.Equals(candidate.Get("Identity"), packageId, StringComparison.OrdinalIgnoreCase));
            return item?.Get("Version");
        }

        internal static PublishPlanningEvaluation Parse(JsonElement root)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Properties", out var propertyElement))
            {
                foreach (var property in propertyElement.EnumerateObject())
                    properties[property.Name] = property.Value.GetString() ?? string.Empty;
            }
            var projectReferences = new List<PublishPlanningItem>();
            var packageReferences = new List<PublishPlanningItem>();
            var packageVersions = new List<PublishPlanningItem>();
            if (root.TryGetProperty("Items", out var items))
            {
                ParseItems(items, "ProjectReference", projectReferences);
                ParseItems(items, "PackageReference", packageReferences);
                ParseItems(items, "PackageVersion", packageVersions);
            }
            return new PublishPlanningEvaluation(properties, projectReferences, packageReferences, packageVersions);
        }

        private static void ParseItems(JsonElement items, string itemType, ICollection<PublishPlanningItem> destination)
        {
            if (!items.TryGetProperty(itemType, out var values))
                return;
            foreach (var value in values.EnumerateArray())
            {
                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in value.EnumerateObject())
                    metadata[property.Name] = property.Value.GetString() ?? string.Empty;
                destination.Add(new PublishPlanningItem(metadata));
            }
        }
    }

    private sealed class PublishPlanningItem
    {
        private readonly IReadOnlyDictionary<string, string> _metadata;
        internal PublishPlanningItem(IReadOnlyDictionary<string, string> metadata) => _metadata = metadata;
        internal string? Get(string name) => _metadata.TryGetValue(name, out var value) ? value : null;
        internal bool IsProjectReferenceExcludedFromPackage()
            => IsFalse("ReferenceOutputAssembly") || IsFalse("BuildReference") || IsFalse("TreatAsPackageReference") || HasPrivateAssetsAll();
        internal bool IsPackageReferenceExcludedFromPackage() => HasPrivateAssetsAll();
        private bool HasPrivateAssetsAll()
            => (Get("PrivateAssets") ?? string.Empty).Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value.Trim(), "all", StringComparison.OrdinalIgnoreCase));
        private bool IsFalse(string name) => string.Equals(Get(name), "false", StringComparison.OrdinalIgnoreCase);
    }
}
