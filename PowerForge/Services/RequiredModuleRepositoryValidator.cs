using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Verifies manifest RequiredModules against a publish repository and optionally mirrors missing modules.
/// </summary>
internal sealed class RequiredModuleRepositoryValidator
{
    private readonly ILogger _logger;
    private readonly PSResourceGetClient _psResourceGet;
    private readonly RequiredModuleRepositoryPublisher _publisher;

    public RequiredModuleRepositoryValidator(
        ILogger logger,
        PSResourceGetClient psResourceGet,
        RequiredModuleRepositoryPublisher publisher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _psResourceGet = psResourceGet ?? throw new ArgumentNullException(nameof(psResourceGet));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public void Validate(
        PublishConfiguration publish,
        string repositoryName,
        RepositoryCredential? credential,
        PublishRepositoryConfiguration? repositoryForPublish,
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult)
    {
        if (publish is null) throw new ArgumentNullException(nameof(publish));
        if (string.IsNullOrWhiteSpace(repositoryName)) throw new ArgumentException("Repository name is required.", nameof(repositoryName));
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (buildResult is null) throw new ArgumentNullException(nameof(buildResult));

        var requiredModules = ModulePublisher.GetRequiredModulesForPublish(buildResult, plan);
        if (requiredModules.Length == 0)
            return;

        if (publish.PublishRequiredModules &&
            string.Equals(repositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "PublishRequiredModules is only supported for private repository targets. Refusing to mirror dependencies to PSGallery.");
        }

        var externalModuleDependencies = GetExternalModulesForPublish(buildResult, plan);
        var missing = new List<string>();
        var sourceRepositoryName = ResolveRequiredModuleSourceRepository(publish);
        var sourceCredential = ResolveRequiredModuleSourceCredential(sourceRepositoryName, repositoryName, credential);
        var mirroredPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requiredModule in requiredModules)
        {
            if (ModulePublisher.ShouldSkipRepositoryDependencyValidation(requiredModule, externalModuleDependencies))
            {
                _logger.Info($"Skipping repository dependency verification for required module '{requiredModule.ModuleName}' because it is listed in ExternalModuleDependencies.");
                continue;
            }

            var versions = FindRequiredModuleVersionsInRepository(
                repositoryName,
                credential,
                requiredModule,
                treatNotFoundAsEmpty: publish.PublishRequiredModules);

            if (!ModulePublisher.HasMatchingRequiredModuleVersion(requiredModule, versions))
            {
                if (publish.PublishRequiredModules)
                {
                    _publisher.PublishRequiredModule(
                        requiredModule,
                        sourceRepositoryName,
                        repositoryName,
                        publish.ApiKey,
                        repositoryForPublish,
                        sourceCredential,
                        credential,
                        mirroredPackages);

                    versions = FindRequiredModuleVersionsInRepository(
                        repositoryName,
                        credential,
                        requiredModule,
                        treatNotFoundAsEmpty: false);
                    if (ModulePublisher.HasMatchingRequiredModuleVersion(requiredModule, versions))
                        continue;
                }

                missing.Add($"{requiredModule.ModuleName} [{ModulePublisher.FormatRequiredModuleConstraint(requiredModule)}]");
            }
        }

        if (missing.Count > 0)
        {
            var message = $"Required module dependency check failed for repository '{repositoryName}'. Missing or incompatible: {string.Join(", ", missing)}.";
            if (!publish.PublishRequiredModules)
                message += $" Enable PublishRequiredModules to mirror missing dependencies from '{sourceRepositoryName}' before publish.";

            throw new InvalidOperationException(message);
        }
    }

    private IReadOnlyList<string> FindRequiredModuleVersionsInRepository(
        string repositoryName,
        RepositoryCredential? credential,
        RequiredModuleReference requiredModule,
        bool treatNotFoundAsEmpty)
    {
        try
        {
            return _psResourceGet.Find(
                    new PSResourceFindOptions(
                        names: new[] { requiredModule.ModuleName },
                        version: RequiredModuleRepositoryPublisher.BuildPSResourceGetVersionRange(requiredModule),
                        prerelease: RequiredModuleRepositoryPublisher.AllowsPrerelease(requiredModule),
                        repositories: new[] { repositoryName },
                        credential: credential),
                    timeout: TimeSpan.FromMinutes(2))
                .Where(r => string.Equals(r.Name, requiredModule.ModuleName, StringComparison.OrdinalIgnoreCase))
                .Select(ModulePublisher.GetRepositoryVersionText)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Where(v => RequiredModuleRepositoryPublisher.AllowsPrerelease(requiredModule) || !RequiredModuleRepositoryPublisher.IsPrereleaseVersion(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex)
        {
            if (treatNotFoundAsEmpty &&
                ModulePublisher.IsRepositoryPackageNotFound(requiredModule.ModuleName, ex))
            {
                _logger.Info($"Required dependency '{requiredModule.ModuleName}' is not present in repository '{repositoryName}' and will be mirrored.");
                return Array.Empty<string>();
            }

            throw new InvalidOperationException(
                $"Failed to verify required dependency '{requiredModule.ModuleName}' in repository '{repositoryName}' before publish. {ex.Message}");
        }
    }

    private static string ResolveRequiredModuleSourceRepository(PublishConfiguration publish)
        => string.IsNullOrWhiteSpace(publish.RequiredModuleSourceRepository)
            ? "PSGallery"
            : publish.RequiredModuleSourceRepository!.Trim();

    private static RepositoryCredential? ResolveRequiredModuleSourceCredential(
        string sourceRepositoryName,
        string targetRepositoryName,
        RepositoryCredential? targetCredential)
    {
        if (targetCredential is null)
            return null;

        if (string.Equals(sourceRepositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            return null;

        return string.Equals(sourceRepositoryName, targetRepositoryName, StringComparison.OrdinalIgnoreCase)
            ? targetCredential
            : null;
    }

    private static HashSet<string> GetExternalModulesForPublish(
        ModuleBuildResult buildResult,
        ModulePipelinePlan plan)
    {
        if (!string.IsNullOrWhiteSpace(buildResult.ManifestPath) &&
            File.Exists(buildResult.ManifestPath) &&
            ModuleManifestValueReader.ReadPsDataStringOrArray(buildResult.ManifestPath, "ExternalModuleDependencies") is { Length: > 0 } manifestExternalModules)
        {
            return manifestExternalModules
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        return (plan.ExternalModuleDependencies ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
