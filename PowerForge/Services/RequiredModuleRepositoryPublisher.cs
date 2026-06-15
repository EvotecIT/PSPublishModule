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
        RepositoryCredential? sourceCredential)
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
                    version: null,
                    prerelease: true,
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
            _psResourceGet.Save(
                new PSResourceSaveOptions(
                    name: moduleName,
                    destinationPath: tempRoot,
                    version: selectedVersion,
                    repository: sourceRepository,
                    prerelease: true,
                    trustRepository: true,
                    skipDependencyCheck: true,
                    acceptLicense: true,
                    quiet: true,
                    credential: sourceCredential),
                timeout: TimeSpan.FromMinutes(10));

            var savedModulePath = FindSavedModulePath(tempRoot, moduleName, selectedVersion);
            if (savedModulePath is null)
                throw new InvalidOperationException($"Save-PSResource completed for required module '{moduleName}' {selectedVersion}, but the saved module manifest was not found under '{tempRoot}'.");

            _repositoryPublisher.Publish(
                new RepositoryPublishRequest
                {
                    Path = savedModulePath,
                    IsNupkg = false,
                    RepositoryName = targetRepositoryNameValue,
                    Tool = PublishTool.PSResourceGet,
                    ApiKey = string.IsNullOrWhiteSpace(targetApiKey) ? null : targetApiKey,
                    Repository = targetRepository,
                    DestinationPath = null,
                    SkipDependenciesCheck = true,
                    SkipModuleManifestValidate = false
                });

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

    internal static PSResourceInfo? SelectRequiredModuleVersionForPublish(
        RequiredModuleReference requiredModule,
        IReadOnlyList<PSResourceInfo> candidates)
    {
        if (requiredModule is null || candidates is null || candidates.Count == 0)
            return null;

        PSResourceInfo? selected = null;
        foreach (var candidate in candidates)
        {
            if (candidate is null ||
                !string.Equals(candidate.Name, requiredModule.ModuleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var versionText = ModulePublisher.GetRepositoryVersionText(candidate);
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

    internal static string? FindSavedModulePath(string rootPath, string moduleName, string versionText)
    {
        if (string.IsNullOrWhiteSpace(rootPath) ||
            string.IsNullOrWhiteSpace(moduleName) ||
            !Directory.Exists(rootPath))
        {
            return null;
        }

        var manifestFileName = moduleName.Trim() + ".psd1";
        var manifests = Directory
            .EnumerateFiles(rootPath, "*.psd1", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), manifestFileName, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToArray();

        if (manifests.Length == 0)
            return null;

        var normalizedVersion = (versionText ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedVersion))
        {
            var versionMatch = manifests.FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), normalizedVersion, StringComparison.OrdinalIgnoreCase));
            if (versionMatch is not null)
                return versionMatch;
        }

        return manifests[0];
    }

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
