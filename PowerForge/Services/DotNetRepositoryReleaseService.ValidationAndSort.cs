using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
        bool usePlannedProjectGraph = false)
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

        var dependencies = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var dependents = byPackageId.Keys.ToDictionary(
            packageId => packageId,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var entry in byPackageId)
        {
            var selectedDependencies = usePlannedProjectGraph
                ? ReadPlannedProjectDependencies(entry.Value, byPackageId)
                : ReadSelectedPackageDependencies(entry.Value, entry.Key, byPackageId);
            foreach (var dependency in selectedDependencies)
            {
                dependencies[entry.Key].Add(dependency);
                dependents[dependency].Add(entry.Key);
            }
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

        if (ordered.Count != projects.Count)
        {
            var cycle = inDegree
                .Where(entry => entry.Value > 0)
                .Select(entry => entry.Key)
                .OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                $"NuGet package dependency cycle detected among selected projects: {string.Join(", ", cycle)}. Publishing stopped before any package was pushed.");
        }

        return ordered;
    }

    private static string GetEffectivePackageId(DotNetRepositoryProjectResult project)
    {
        var packageId = string.IsNullOrWhiteSpace(project.PackageId)
            ? project.ProjectName
            : project.PackageId;
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
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                           !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
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
                    if (dependencyId is not null && dependencyId.Length > 0 && selectedPackages.ContainsKey(dependencyId))
                        dependencies.Add(dependencyId);
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

        return dependencies;
    }

    private static IReadOnlyCollection<string> ReadPlannedProjectDependencies(
        DotNetRepositoryProjectResult project,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages)
    {
        if (string.IsNullOrWhiteSpace(project.CsprojPath) || !File.Exists(project.CsprojPath))
            throw new InvalidOperationException($"Cannot determine a safe planned NuGet publish order for '{GetEffectivePackageId(project)}' because its project file does not exist.");

        var selectedProjectPaths = selectedPackages.ToDictionary(
            entry => Path.GetFullPath(entry.Value.CsprojPath),
            entry => entry.Key,
            StringComparer.OrdinalIgnoreCase);
        var dependencies = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var initialEvaluation = EvaluatePublishPlanningItems(project.CsprojPath, targetFramework: null);
        AddPlannedDependencies(initialEvaluation, selectedPackages, selectedProjectPaths, dependencies);

        foreach (var targetFramework in initialEvaluation.TargetFrameworks)
        {
            var evaluation = EvaluatePublishPlanningItems(project.CsprojPath, targetFramework);
            AddPlannedDependencies(evaluation, selectedPackages, selectedProjectPaths, dependencies);
        }

        return dependencies;
    }

    private static void AddPlannedDependencies(
        PublishPlanningEvaluation evaluation,
        IReadOnlyDictionary<string, DotNetRepositoryProjectResult> selectedPackages,
        IReadOnlyDictionary<string, string> selectedProjectPaths,
        ISet<string> dependencies)
    {
        foreach (var projectReference in evaluation.ProjectReferences)
        {
            if (selectedProjectPaths.TryGetValue(Path.GetFullPath(projectReference), out var packageId))
                dependencies.Add(packageId);
        }

        foreach (var packageReference in evaluation.PackageReferences)
        {
            if (selectedPackages.ContainsKey(packageReference))
                dependencies.Add(packageReference);
        }
    }

    private static PublishPlanningEvaluation EvaluatePublishPlanningItems(string projectPath, string? targetFramework)
    {
        var arguments = new List<string>
        {
            "msbuild",
            projectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:TargetFrameworks",
            "-getProperty:TargetFramework",
            "-getItem:ProjectReference",
            "-getItem:PackageReference",
            "-p:BuildProjectReferences=false"
        };
        if (!string.IsNullOrWhiteSpace(targetFramework))
            arguments.Add($"-p:TargetFramework={targetFramework}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"dotnet msbuild could not be started to evaluate '{projectPath}'.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Cannot determine a safe planned NuGet publish order because dotnet msbuild could not evaluate '{projectPath}' ({targetFramework ?? "outer build"}, exit {process.ExitCode}): {standardError.Trim()}");
        }

        var jsonStart = standardOutput.IndexOf('{');
        var jsonEnd = standardOutput.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd < jsonStart)
            throw new InvalidOperationException($"dotnet msbuild returned invalid planning metadata for '{projectPath}'.");

        try
        {
            using var document = JsonDocument.Parse(standardOutput.Substring(jsonStart, jsonEnd - jsonStart + 1));
            var root = document.RootElement;
            var properties = root.GetProperty("Properties");
            var targetFrameworksValue = properties.TryGetProperty("TargetFrameworks", out var targetFrameworksProperty)
                ? targetFrameworksProperty.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(targetFrameworksValue) &&
                properties.TryGetProperty("TargetFramework", out var targetFrameworkProperty))
            {
                targetFrameworksValue = targetFrameworkProperty.GetString();
            }

            var targetFrameworks = (targetFrameworksValue ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var projectReferences = new List<string>();
            var packageReferences = new List<string>();
            if (root.TryGetProperty("Items", out var items))
            {
                if (items.TryGetProperty("ProjectReference", out var references))
                {
                    foreach (var reference in references.EnumerateArray())
                    {
                        if (reference.TryGetProperty("FullPath", out var fullPath) && !string.IsNullOrWhiteSpace(fullPath.GetString()))
                            projectReferences.Add(fullPath.GetString()!);
                    }
                }

                if (items.TryGetProperty("PackageReference", out var packages))
                {
                    foreach (var package in packages.EnumerateArray())
                    {
                        if (package.TryGetProperty("Identity", out var identity) && !string.IsNullOrWhiteSpace(identity.GetString()))
                            packageReferences.Add(identity.GetString()!);
                    }
                }
            }

            return new PublishPlanningEvaluation(targetFrameworks, projectReferences, packageReferences);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidOperationException($"dotnet msbuild returned invalid planning metadata for '{projectPath}': {exception.Message}", exception);
        }
    }

    private sealed class PublishPlanningEvaluation
    {
        internal PublishPlanningEvaluation(
            IReadOnlyList<string> targetFrameworks,
            IReadOnlyList<string> projectReferences,
            IReadOnlyList<string> packageReferences)
        {
            TargetFrameworks = targetFrameworks;
            ProjectReferences = projectReferences;
            PackageReferences = packageReferences;
        }

        internal IReadOnlyList<string> TargetFrameworks { get; }

        internal IReadOnlyList<string> ProjectReferences { get; }

        internal IReadOnlyList<string> PackageReferences { get; }
    }

}
