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

}
