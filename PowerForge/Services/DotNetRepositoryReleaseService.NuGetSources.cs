using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Configuration;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static bool TryResolveNamedLocalPublishSource(
        string sourceName,
        string searchRoot,
        out string resolvedSource)
    {
        resolvedSource = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(searchRoot))
            return false;

        try
        {
            var settingsRoot = Path.GetFullPath(searchRoot);
            if (!Directory.Exists(settingsRoot))
                settingsRoot = Path.GetDirectoryName(settingsRoot) ?? settingsRoot;

            var settings = Settings.LoadDefaultSettings(settingsRoot);
            var configuredSource = new PackageSourceProvider(settings)
                .LoadPackageSources()
                .FirstOrDefault(source => string.Equals(
                    source.Name,
                    sourceName,
                    StringComparison.OrdinalIgnoreCase))
                ?.Source;
            if (string.IsNullOrWhiteSpace(configuredSource))
                return false;
            var configuredSourceValue = configuredSource!;

            if (Uri.TryCreate(configuredSourceValue, UriKind.Absolute, out var configuredUri))
            {
                if (!configuredUri.IsFile)
                    return false;

                resolvedSource = Path.GetFullPath(configuredUri.LocalPath);
                return true;
            }

            var normalized = PathValueResolver.NormalizeSeparators(configuredSourceValue);
            resolvedSource = Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : PathValueResolver.Resolve(settingsRoot, normalized);
            return true;
        }
        catch
        {
            // dotnet nuget push reports malformed or inaccessible configuration itself.
            return false;
        }
    }

    /// <summary>
    /// Finds the primary package paired with a symbol package in an attempted publish set.
    /// </summary>
    internal static string? FindPrimaryPackageForSymbol(
        string packagePath,
        IEnumerable<string> packageCandidates)
    {
        if (string.IsNullOrWhiteSpace(packagePath) ||
            !packagePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var packageName = Path.GetFileNameWithoutExtension(packagePath);
        return packageCandidates.FirstOrDefault(candidate =>
            candidate.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetFileNameWithoutExtension(candidate),
                packageName,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines whether a symbol package may be published after its matching primary package.
    /// </summary>
    internal static bool CanPublishSymbolPackage(
        string packagePath,
        IEnumerable<string> packageCandidates,
        Func<string, bool> primarySucceeded,
        out string? primaryPackagePath)
    {
        if (!packagePath.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
        {
            primaryPackagePath = null;
            return true;
        }

        primaryPackagePath = FindPrimaryPackageForSymbol(packagePath, packageCandidates);
        return primaryPackagePath is not null && primarySucceeded(primaryPackagePath);
    }

    /// <summary>
    /// Creates the failure used when a companion symbol cannot follow its primary package.
    /// </summary>
    internal static PackagePushResult CreateBlockedCompanionResult(string packagePath, string? primaryPackagePath)
        => new()
        {
            Outcome = PackagePushOutcome.Failed,
            Message = string.IsNullOrWhiteSpace(primaryPackagePath)
                ? $"Symbol package '{Path.GetFileName(packagePath)}' has no matching primary package in the publish set."
                : $"Symbol package '{Path.GetFileName(packagePath)}' was not published because primary package '{Path.GetFileName(primaryPackagePath)}' did not publish successfully."
        };
}
