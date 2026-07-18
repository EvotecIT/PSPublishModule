using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private string[] ResolveSynchronizedReleaseOperationFingerprints(ModulePipelinePlan plan)
    {
        var fingerprints = new List<string>
        {
            CreateSynchronizedReleaseFingerprint(
                "Release",
                plan.Release?.Configuration?.StageRoot,
                plan.Release?.Configuration?.VersionSource.ToString(),
                plan.Release?.Configuration?.PrimaryProject,
                plan.Release?.Configuration?.Version,
                plan.Release?.Configuration?.SynchronizeModuleVersion.ToString(),
                string.Join(",", plan.Release?.Configuration?.BuildOrder ?? Array.Empty<string>()),
                string.Join(",", plan.Release?.Configuration?.PublishOrder ?? Array.Empty<string>()))
        };

        foreach (var publish in plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
        {
            if (publish?.Configuration is null)
                continue;

            fingerprints.Add(CreateModulePublishOperationFingerprint(plan, publish));
        }

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            var reference = segment?.Configuration;
            if (reference is null || string.IsNullOrWhiteSpace(reference.ConfigPath))
                continue;

            var configPath = ResolvePackageBuildPath(plan.ProjectRoot, reference.ConfigPath);
            var configuration = LoadProjectBuildConfiguration(configPath, reference);
            var actions = ResolveEffectiveActions(configuration);
            var laneLabel = reference.Name ?? configPath;
            var checkpointKey = ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.ProjectBuild,
                segment!,
                laneLabel);
            fingerprints.Add(CreateSynchronizedReleaseFingerprint(
                "ProjectBuildControl",
                checkpointKey,
                reference.Enabled.ToString(),
                reference.BuildBeforeModule.ToString(),
                reference.UseAsReleaseVersionSource.ToString(),
                reference.ProvideLocalNuGetFeed.ToString()));
            fingerprints.Add(CreatePackageLaneConfigurationFingerprint(
                "ProjectBuild",
                laneLabel,
                checkpointKey,
                configPath,
                configuration));
            if (actions.PublishNuGet)
            {
                fingerprints.Add(CreatePackagePublishOperationFingerprint(
                    "ProjectBuild",
                    laneLabel,
                    checkpointKey,
                    configPath,
                    ReleasePublishDestination.NuGet,
                    configuration));
            }
            if (actions.PublishGitHub)
            {
                fingerprints.Add(CreatePackagePublishOperationFingerprint(
                    "ProjectBuild",
                    laneLabel,
                    checkpointKey,
                    configPath,
                    ReleasePublishDestination.GitHub,
                    configuration));
            }
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            var packageBuild = segment?.Configuration;
            if (packageBuild is null)
                continue;

            var configuration = MapPackageBuildConfiguration(packageBuild, plan.ProjectRoot);
            var actions = ResolveEffectiveActions(configuration);
            var inlineConfigPath = Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
            var laneLabel = packageBuild.Name ?? inlineConfigPath;
            var checkpointKey = ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.PackageBuild,
                segment!,
                laneLabel);
            fingerprints.Add(CreateSynchronizedReleaseFingerprint(
                "PackageBuildControl",
                checkpointKey,
                packageBuild.Enabled.ToString(),
                packageBuild.BuildBeforeModule.ToString(),
                packageBuild.UseAsReleaseVersionSource.ToString(),
                packageBuild.ProvideLocalNuGetFeed.ToString()));
            fingerprints.Add(CreatePackageLaneConfigurationFingerprint(
                "PackageBuild",
                laneLabel,
                checkpointKey,
                inlineConfigPath,
                configuration));
            if (actions.PublishNuGet)
            {
                fingerprints.Add(CreatePackagePublishOperationFingerprint(
                    "PackageBuild",
                    laneLabel,
                    checkpointKey,
                    inlineConfigPath,
                    ReleasePublishDestination.NuGet,
                    configuration));
            }
            if (actions.PublishGitHub)
            {
                fingerprints.Add(CreatePackagePublishOperationFingerprint(
                    "PackageBuild",
                    laneLabel,
                    checkpointKey,
                    inlineConfigPath,
                    ReleasePublishDestination.GitHub,
                    configuration));
            }
        }

        return fingerprints
            .OrderBy(static fingerprint => fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string[] ResolveSynchronizedReleasePublishOperationKeys(ModulePipelinePlan plan)
    {
        var operations = new List<string>();
        foreach (var publish in plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
        {
            if (publish?.Configuration is null)
                continue;

            if (publish.Configuration.Destination is PublishDestination.PowerShellGallery or PublishDestination.GitHub)
            {
                operations.Add(CreateModulePublishOperationFingerprint(plan, publish));
            }
        }

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null)
                continue;

            if (ShouldExecuteProjectBuildPublish(plan, segment, PackageBuildPublishDestination.NuGet))
            {
                operations.Add(CreateProjectBuildPublishOperationFingerprint(
                    plan,
                    segment,
                    PackageBuildPublishDestination.NuGet));
            }
            if (ShouldExecuteProjectBuildPublish(plan, segment, PackageBuildPublishDestination.GitHub))
            {
                operations.Add(CreateProjectBuildPublishOperationFingerprint(
                    plan,
                    segment,
                    PackageBuildPublishDestination.GitHub));
            }
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null)
                continue;

            if (ShouldExecutePackageBuildPublish(plan, segment, PackageBuildPublishDestination.NuGet))
            {
                operations.Add(CreatePackageBuildPublishOperationFingerprint(
                    plan,
                    segment,
                    PackageBuildPublishDestination.NuGet));
            }
            if (ShouldExecutePackageBuildPublish(plan, segment, PackageBuildPublishDestination.GitHub))
            {
                operations.Add(CreatePackageBuildPublishOperationFingerprint(
                    plan,
                    segment,
                    PackageBuildPublishDestination.GitHub));
            }
        }

        return operations.ToArray();
    }

    private static string CreateModulePublishOperationFingerprint(
        ModulePipelinePlan plan,
        ConfigurationPublishSegment publish)
    {
        var index = Array.IndexOf(plan.Publishes, publish);
        if (index < 0)
            throw new InvalidOperationException("Module publish operation is not present in the active module pipeline plan.");

        return CreateSynchronizedReleaseFingerprint(
            "ModulePublishOperation",
            index.ToString(),
            CreateModulePublishConfigurationFingerprint(publish.Configuration));
    }

    private static string CreateModulePublishConfigurationFingerprint(PublishConfiguration publish)
    {
        var repository = publish.Repository;
        return CreateSynchronizedReleaseFingerprint(
            "Module",
            publish.Enabled.ToString(),
            publish.Destination.ToString(),
            publish.Tool.ToString(),
            publish.ID,
            publish.UserName,
            publish.RepositoryName,
            repository?.Name,
            repository?.Uri,
            repository?.SourceUri,
            repository?.PublishUri,
            repository?.ApiVersion.ToString(),
            repository?.Trusted.ToString(),
            repository?.Priority?.ToString(),
            repository?.EnsureRegistered.ToString(),
            repository?.UnregisterAfterUse.ToString(),
            repository?.CredentialProvider?.Kind.ToString(),
            repository?.CredentialProvider?.JFrogPlatformUri,
            repository?.CredentialProvider?.JFrogOidcProvider,
            repository?.CredentialProvider?.JFrogOidcProviderType.ToString(),
            publish.OverwriteTagName,
            publish.DoNotMarkAsPreRelease.ToString(),
            publish.GenerateReleaseNotes.ToString(),
            publish.UseAsDependencyVersionSource.ToString(),
            publish.PublishRequiredModules.ToString(),
            publish.RequiredModuleSourceRepository,
            publish.RequiredModuleSourceRepositoryUri,
            publish.Force.ToString());
    }

    private string CreateProjectBuildPublishOperationFingerprint(
        ModulePipelinePlan plan,
        ConfigurationProjectBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        var reference = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        if (string.IsNullOrWhiteSpace(reference.ConfigPath))
            throw new InvalidOperationException("ProjectBuild ConfigPath is required.");

        var configPath = ResolvePackageBuildPath(plan.ProjectRoot, reference.ConfigPath);
        var configuration = LoadProjectBuildConfiguration(configPath, reference);
        var laneLabel = reference.Name ?? configPath;
        var checkpointKey = ResolveSynchronizedReleaseLaneKey(
            plan,
            ReleaseVersionSource.ProjectBuild,
            segment,
            laneLabel);
        return CreatePackagePublishOperationFingerprint(
            "ProjectBuild",
            laneLabel,
            checkpointKey,
            configPath,
            ToReleasePublishDestination(destination),
            configuration);
    }

    private static string CreatePackageBuildPublishOperationFingerprint(
        ModulePipelinePlan plan,
        ConfigurationPackageBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        var packageBuild = segment.Configuration ?? throw new InvalidOperationException("PackageBuild configuration is missing.");
        var configuration = MapPackageBuildConfiguration(packageBuild, plan.ProjectRoot);
        var configPath = Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
        var laneLabel = packageBuild.Name ?? configPath;
        var checkpointKey = ResolveSynchronizedReleaseLaneKey(
            plan,
            ReleaseVersionSource.PackageBuild,
            segment,
            laneLabel);
        return CreatePackagePublishOperationFingerprint(
            "PackageBuild",
            laneLabel,
            checkpointKey,
            configPath,
            ToReleasePublishDestination(destination),
            configuration);
    }

    private static ReleasePublishDestination ToReleasePublishDestination(PackageBuildPublishDestination destination)
        => destination == PackageBuildPublishDestination.NuGet
            ? ReleasePublishDestination.NuGet
            : ReleasePublishDestination.GitHub;

    private static string CreatePackagePublishOperationFingerprint(
        string laneType,
        string laneLabel,
        string checkpointKey,
        string configurationPath,
        ReleasePublishDestination destination,
        ProjectBuildConfiguration configuration)
        => CreateSynchronizedReleaseFingerprint(
            laneType,
            laneLabel,
            checkpointKey,
            configurationPath,
            destination.ToString(),
            configuration.RootPath,
            configuration.PublishSource,
            configuration.UseGitHubPackages.ToString(),
            configuration.GitHubPackagesOwner,
            configuration.GitHubUsername,
            configuration.GitHubRepositoryName,
            configuration.GitHubReleaseMode,
            configuration.GitHubPrimaryProject,
            configuration.GitHubTagName,
            configuration.GitHubTagTemplate,
            configuration.GitHubTagConflictPolicy);

    private static string CreatePackageLaneConfigurationFingerprint(
        string laneType,
        string laneLabel,
        string checkpointKey,
        string configurationPath,
        ProjectBuildConfiguration configuration)
        => CreateSynchronizedReleaseFingerprint(
            "PackageLane",
            laneType,
            laneLabel,
            checkpointKey,
            configurationPath,
            configuration.RootPath,
            configuration.ExpectedVersion,
            SerializeSynchronizedReleaseStringMap(configuration.ExpectedVersionMap),
            SerializeSynchronizedReleaseVersionTracks(configuration.VersionTracks),
            configuration.ExpectedVersionMapAsInclude.ToString(),
            configuration.ExpectedVersionMapUseWildcards.ToString(),
            configuration.AlignPackageVersions.ToString(),
            SerializeSynchronizedReleaseValues(configuration.IncludeProjects),
            SerializeSynchronizedReleaseValues(configuration.ExcludeProjects),
            SerializeSynchronizedReleaseValues(configuration.ExcludeDirectories),
            SerializeSynchronizedReleaseValues(configuration.NugetSource),
            configuration.IncludePrerelease.ToString(),
            configuration.Configuration,
            configuration.OutputPath,
            configuration.ReleaseZipOutputPath,
            configuration.StagingPath,
            configuration.CleanStaging?.ToString(),
            configuration.PlanOnly?.ToString(),
            configuration.PlanOutputPath,
            configuration.UpdateVersions?.ToString(),
            configuration.Build?.ToString(),
            configuration.PackStrategy,
            configuration.IncludeSymbols?.ToString(),
            configuration.PublishNuget?.ToString(),
            configuration.PublishGitHub?.ToString(),
            configuration.CreateReleaseZip?.ToString(),
            configuration.UseGitHubPackages.ToString(),
            configuration.GitHubPackagesOwner,
            configuration.PublishSource,
            configuration.SkipDuplicate?.ToString(),
            configuration.PublishFailFast?.ToString(),
            configuration.CertificateThumbprint,
            configuration.CertificateStore,
            configuration.TimeStampServer,
            configuration.SignAssemblies?.ToString(),
            configuration.SignDependencyAssemblies?.ToString(),
            configuration.SignPackages?.ToString(),
            configuration.GitHubUsername,
            configuration.GitHubRepositoryName,
            configuration.GitHubIsPreRelease.ToString(),
            configuration.GitHubIncludeProjectNameInTag.ToString(),
            configuration.GitHubGenerateReleaseNotes.ToString(),
            configuration.GitHubReleaseName,
            configuration.GitHubTagName,
            configuration.GitHubTagTemplate,
            configuration.GitHubReleaseMode,
            configuration.GitHubPrimaryProject,
            configuration.GitHubTagConflictPolicy);

    private static string SerializeSynchronizedReleaseStringMap(
        IReadOnlyDictionary<string, string>? values)
        => values is null
            ? string.Empty
            : JsonSerializer.Serialize(values
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => new[] { entry.Key, entry.Value })
                .ToArray());

    private static string SerializeSynchronizedReleaseVersionTracks(
        IReadOnlyDictionary<string, ProjectBuildVersionTrack>? tracks)
        => tracks is null
            ? string.Empty
            : JsonSerializer.Serialize(tracks
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => new[]
                {
                    entry.Key,
                    entry.Value?.ExpectedVersion ?? string.Empty,
                    entry.Value?.AnchorProject ?? string.Empty,
                    entry.Value?.AnchorPackageId ?? string.Empty,
                    SerializeSynchronizedReleaseValues(entry.Value?.Projects),
                    SerializeSynchronizedReleaseValues(entry.Value?.NugetSource),
                    entry.Value?.IncludePrerelease?.ToString() ?? string.Empty
                })
                .ToArray());

    private static string SerializeSynchronizedReleaseValues(IEnumerable<string>? values)
        => values is null
            ? string.Empty
            : JsonSerializer.Serialize(values.Select(static value => value?.Trim() ?? string.Empty).ToArray());

    private static string CreateSynchronizedReleaseFingerprint(params string?[] values)
    {
        var serialized = JsonSerializer.Serialize(
            values.Select(static value => value?.Trim() ?? string.Empty).ToArray());
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(serialized)))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

}
