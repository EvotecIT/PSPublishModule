using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Mirrors manifest RequiredModules from a source repository into the target publish repository.
/// </summary>
internal sealed class RequiredModuleRepositoryPublisher
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly RepositoryPublisher _repositoryPublisher;

    public RequiredModuleRepositoryPublisher(
        ILogger logger,
        PSResourceGetClient psResourceGet,
        RepositoryPublisher repositoryPublisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _psResourceGet = psResourceGet ?? throw new ArgumentNullException(nameof(psResourceGet));
        _repositoryPublisher = repositoryPublisher ?? throw new ArgumentNullException(nameof(repositoryPublisher));
    }

    public void PublishRequiredModule(
        RequiredModuleReference requiredModule,
        string sourceRepositoryName,
        string targetRepositoryName,
        string? targetApiKey,
        PublishRepositoryConfiguration? targetRepository,
        RepositoryCredential? sourceCredential,
        ISet<string>? mirroredPackages = null)
    {
        if (requiredModule is null) throw new ArgumentNullException(nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(requiredModule.ModuleName)) throw new ArgumentException("Required module name is required.", nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(sourceRepositoryName)) throw new ArgumentException("Source repository name is required.", nameof(sourceRepositoryName));
        if (string.IsNullOrWhiteSpace(targetRepositoryName)) throw new ArgumentException("Target repository name is required.", nameof(targetRepositoryName));

        var moduleName = requiredModule.ModuleName.Trim();
        var sourceRepository = sourceRepositoryName.Trim();
        var targetRepositoryNameValue = targetRepositoryName.Trim();

        _logger.Info($"Publishing missing required module '{moduleName}' from repository '{sourceRepository}' to repository '{targetRepositoryNameValue}'.");

        var sourceItems = _psResourceGet.Find(
                new PSResourceFindOptions(
                    names: new[] { moduleName },
                    version: BuildPSResourceGetVersionRange(requiredModule),
                    prerelease: AllowsPrerelease(requiredModule),
                    repositories: new[] { sourceRepository },
                    credential: sourceCredential),
                timeout: TimeSpan.FromMinutes(2))
            .Where(r => string.Equals(r.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var selected = SelectRequiredModuleVersionForPublish(requiredModule, sourceItems);
        if (selected is null)
        {
            throw new InvalidOperationException(
                $"Required module '{moduleName}' is missing in repository '{targetRepositoryNameValue}', and no matching version was found in source repository '{sourceRepository}'. Constraint: {ModulePublisher.FormatRequiredModuleConstraint(requiredModule)}.");
        }

        var selectedVersion = ModulePublisher.GetRepositoryVersionText(selected);
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "required-modules", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            var savedItems = _psResourceGet.Save(
                new PSResourceSaveOptions(
                    name: moduleName,
                    destinationPath: tempRoot,
                    version: selectedVersion,
                    repository: sourceRepository,
                    prerelease: IsPrereleaseVersion(selectedVersion),
                    trustRepository: true,
                    skipDependencyCheck: false,
                    acceptLicense: true,
                    quiet: true,
                    credential: sourceCredential),
                timeout: TimeSpan.FromMinutes(10));

            var savedModulePath = FindSavedModulePath(tempRoot, moduleName, selectedVersion);
            if (savedModulePath is null)
                throw new InvalidOperationException($"Save-PSResource completed for required module '{moduleName}' {selectedVersion}, but the saved module manifest was not found under '{tempRoot}'.");

            foreach (var package in FindSavedModulePackagesForPublish(tempRoot, savedItems, moduleName, selectedVersion))
            {
                var packageKey = $"{package.Name}|{package.Version}";
                if (mirroredPackages is not null && !mirroredPackages.Add(packageKey))
                {
                    _logger.Info($"Skipping already mirrored required module package '{package.Name}' {package.Version}.");
                    continue;
                }

                _repositoryPublisher.Publish(
                    new RepositoryPublishRequest
                    {
                        Path = package.Path,
                        IsNupkg = false,
                        RepositoryName = targetRepositoryNameValue,
                        Tool = PublishTool.PSResourceGet,
                        ApiKey = string.IsNullOrWhiteSpace(targetApiKey) ? null : targetApiKey,
                        Repository = targetRepository,
                        DestinationPath = null,
                        SkipDependenciesCheck = true,
                        SkipModuleManifestValidate = false
                    });
            }

            _logger.Info($"Published required module '{moduleName}' {selectedVersion} to repository '{targetRepositoryNameValue}'.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    internal static string? BuildPSResourceGetVersionRange(RequiredModuleReference requiredModule)
    {
        if (requiredModule is null)
            return null;

        if (!string.IsNullOrWhiteSpace(requiredModule.RequiredVersion))
            return $"[{requiredModule.RequiredVersion!.Trim()}]";

        var minimum = string.IsNullOrWhiteSpace(requiredModule.ModuleVersion)
            ? null
            : requiredModule.ModuleVersion!.Trim();
        var maximum = string.IsNullOrWhiteSpace(requiredModule.MaximumVersion)
            ? null
            : requiredModule.MaximumVersion!.Trim();

        if (minimum is not null && maximum is not null)
            return $"[{minimum},{maximum}]";
        if (minimum is not null)
            return $"[{minimum},)";
        if (maximum is not null)
            return $"(,{maximum}]";

        return null;
    }

    internal static PSResourceInfo? SelectRequiredModuleVersionForPublish(
        RequiredModuleReference requiredModule,
        IReadOnlyList<PSResourceInfo> candidates)
    {
        if (requiredModule is null || candidates is null || candidates.Count == 0)
            return null;

        PSResourceInfo? selected = null;
        var allowPrerelease = AllowsPrerelease(requiredModule);
        foreach (var candidate in candidates)
        {
            if (candidate is null ||
                !string.Equals(candidate.Name, requiredModule.ModuleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionText = ModulePublisher.GetRepositoryVersionText(candidate);
            if (!allowPrerelease && IsPrereleaseVersion(versionText))
                continue;

            if (!ModulePublisher.DoesVersionMatchRequiredModule(requiredModule, versionText))
                continue;

            if (selected is null ||
                CompareRepositoryVersions(versionText, ModulePublisher.GetRepositoryVersionText(selected)) > 0)
            {
                selected = candidate;
            }
        }

        return selected;
    }

    internal static bool AllowsPrerelease(RequiredModuleReference requiredModule)
    {
        if (requiredModule is null)
            return false;

        return IsPrereleaseVersion(requiredModule.RequiredVersion) ||
               IsPrereleaseVersion(requiredModule.ModuleVersion) ||
               IsPrereleaseVersion(requiredModule.MaximumVersion);
    }

    internal static string? FindSavedModulePath(string rootPath, string moduleName, string versionText)
    {
        if (string.IsNullOrWhiteSpace(rootPath) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            !Directory.Exists(rootPath))
        {
            return null;
        }

        var manifestFileName = moduleName.Trim() + ".psd1";
        var normalizedVersion = (versionText ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            var expectedPath = Path.Combine(rootPath, moduleName.Trim(), normalizedVersion);
            if (File.Exists(Path.Combine(expectedPath, manifestFileName)))
                return expectedPath;
        }

        var manifests = Directory
            .EnumerateFiles(rootPath, "*.psd1", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), manifestFileName, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

        if (manifests.Length == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            var versionMatch = manifests.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), normalizedVersion, StringComparison.OrdinalIgnoreCase));
            if (versionMatch is not null)
                return versionMatch;
        }

        return manifests[0];
    }

    internal static IReadOnlyList<SavedRequiredModulePackage> FindSavedModulePackagesForPublish(
        string rootPath,
        IReadOnlyList<PSResourceInfo> savedItems,
        string primaryModuleName,
        string primaryVersionText)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<SavedRequiredModulePackage>();

        var packages = (savedItems ?? Array.Empty<PSResourceInfo>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Name))
            .Select(item =>
            {
                var version = ModulePublisher.GetRepositoryVersionText(item);
                var path = FindSavedModulePath(rootPath, item.Name, version);
                return path is null
                    ? null
                    : new SavedRequiredModulePackage(item.Name.Trim(), version, path);
            })
            .Where(package => package is not null)
            .Select(package => package!)
            .GroupBy(package => $"{package.Name}|{package.Version}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var primary = packages.FirstOrDefault(package =>
            string.Equals(package.Name, primaryModuleName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(package.Version, primaryVersionText, StringComparison.OrdinalIgnoreCase));
        if (primary is null)
        {
            var primaryPath = FindSavedModulePath(rootPath, primaryModuleName, primaryVersionText);
            if (primaryPath is not null)
            {
                primary = new SavedRequiredModulePackage(primaryModuleName.Trim(), primaryVersionText.Trim(), primaryPath);
                packages.Add(primary);
            }
        }

        var dependencies = packages
            .Where(package => primary is null || !string.Equals(package.Path, primary.Path, StringComparison.OrdinalIgnoreCase))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Version, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (primary is not null)
            dependencies.Add(primary);

        return dependencies;
    }

    internal sealed record SavedRequiredModulePackage(string Name, string Version, string Path);

    private static int CompareRepositoryVersions(string? left, string? right)
    {
        if (TryParseVersion(left, out var leftVersion, out var leftPreRelease) &&
            TryParseVersion(right, out var rightVersion, out var rightPreRelease))
        {
            var versionComparison = leftVersion.CompareTo(rightVersion);
            if (versionComparison != 0)
                return versionComparison;

            if (leftPreRelease is null && rightPreRelease is null) return 0;
            if (leftPreRelease is null) return 1;
            if (rightPreRelease is null) return -1;

            return ComparePreRelease(leftPreRelease, rightPreRelease);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static bool TryParseVersion(string? value, out Version version, out string? preRelease)
    {
        version = new Version(0, 0);
        preRelease = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value!.Trim();
        var dash = text.IndexOf('-');
        var versionText = dash >= 0 ? text.Substring(0, dash) : text;
        preRelease = dash >= 0 ? text.Substring(dash + 1) : null;

        if (!Version.TryParse(versionText, out var parsed))
            return false;

        version = parsed;
        return true;
    }

    private static bool IsPrereleaseVersion(string? value)
        => !string.IsNullOrWhiteSpace(value) && value!.IndexOf('-', StringComparison.Ordinal) >= 0;

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < length; i++)
        {
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;

            var leftNumber = int.TryParse(leftParts[i], out var leftValue);
            var rightNumber = int.TryParse(rightParts[i], out var rightValue);
            if (leftNumber && rightNumber)
            {
                var comparison = leftValue.CompareTo(rightValue);
                if (comparison != 0)
                    return comparison;
            }
            else
            {
                var comparison = StringComparer.OrdinalIgnoreCase.Compare(leftParts[i], rightParts[i]);
                if (comparison != 0)
                    return comparison;
            }
        }

        return 0;
    }
}
