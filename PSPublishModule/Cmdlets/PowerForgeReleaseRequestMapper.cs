using System;
using System.Collections.Generic;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

internal static class PowerForgeReleaseRequestMapper
{
    internal static PowerForgeReleaseRequest Build(
        string configFullPath,
        PowerForgeReleaseRequest? defaults,
        PowerForgeReleaseInvocationOptions options)
    {
        var request = Clone(defaults);
        request.ConfigPath = configFullPath;
        request.PlanOnly = options.PlanOnly;
        request.ValidateOnly = options.ValidateOnly;
        request.PackagesOnly = options.PackagesOnly;
        request.ModuleOnly = options.ModuleOnly;
        request.ToolsOnly = ChooseBool(request.ToolsOnly, options.ToolsOnly) ?? false;

        request.PublishNuget = ChooseBool(request.PublishNuget, options.PublishNuget);
        request.PublishProjectGitHub = ChooseBool(request.PublishProjectGitHub, options.PublishProjectGitHub);
        request.PublishToolGitHub = ChooseBool(request.PublishToolGitHub, options.PublishToolGitHub);
        request.ModuleNoDotnetBuild = ChooseBool(request.ModuleNoDotnetBuild, options.ModuleNoDotnetBuild);
        request.ModuleNoSign = ChooseBool(request.ModuleNoSign, options.ModuleNoSign);
        request.ModuleSignModule = ChooseBool(request.ModuleSignModule, options.ModuleSignModule);
        request.KeepSymbols = ChooseBool(request.KeepSymbols, options.KeepSymbols);
        request.EnableSigning = ChooseBool(request.EnableSigning, options.EnableSigning);

        request.SkipWorkspaceValidation = request.SkipWorkspaceValidation || options.SkipWorkspaceValidation;
        request.SkipRestore = request.SkipRestore || options.SkipRestore;
        request.SkipBuild = request.SkipBuild || options.SkipBuild;
        request.SkipReleaseChecksums = request.SkipReleaseChecksums || options.SkipReleaseChecksums;

        request.Configuration = ChooseString(request.Configuration, options.Configuration);
        request.ModuleVersion = ChooseString(request.ModuleVersion, options.ModuleVersion);
        request.ModulePreReleaseTag = ChooseString(request.ModulePreReleaseTag, options.ModulePreReleaseTag);
        request.WorkspaceConfigPath = ChooseString(request.WorkspaceConfigPath, options.WorkspaceConfigPath);
        request.WorkspaceProfile = ChooseString(request.WorkspaceProfile, options.WorkspaceProfile);
        request.OutputRoot = ChooseString(request.OutputRoot, options.OutputRoot);
        request.StageRoot = ChooseString(request.StageRoot, options.StageRoot);
        request.ManifestJsonPath = ChooseString(request.ManifestJsonPath, options.ManifestJsonPath);
        request.AllowOutputOutsideProjectRoot = request.AllowOutputOutsideProjectRoot || options.AllowOutputOutsideProjectRoot;
        request.AllowManifestOutsideProjectRoot = request.AllowManifestOutsideProjectRoot || options.AllowManifestOutsideProjectRoot;
        request.ChecksumsPath = ChooseString(request.ChecksumsPath, options.ChecksumsPath);
        request.SignProfile = ChooseString(request.SignProfile, options.SignProfile);
        request.SignToolPath = ChooseString(request.SignToolPath, options.SignToolPath);
        request.SignThumbprint = ChooseString(request.SignThumbprint, options.SignThumbprint);
        request.SignSubjectName = ChooseString(request.SignSubjectName, options.SignSubjectName);
        request.SignTimestampUrl = ChooseString(request.SignTimestampUrl, options.SignTimestampUrl);
        request.SignDescription = ChooseString(request.SignDescription, options.SignDescription);
        request.SignUrl = ChooseString(request.SignUrl, options.SignUrl);
        request.SignCsp = ChooseString(request.SignCsp, options.SignCsp);
        request.SignKeyContainer = ChooseString(request.SignKeyContainer, options.SignKeyContainer);
        request.PackageSignThumbprint = ChooseString(request.PackageSignThumbprint, options.PackageSignThumbprint);
        request.PackageSignStore = ChooseString(request.PackageSignStore, options.PackageSignStore);
        request.PackageSignTimestampUrl = ChooseString(request.PackageSignTimestampUrl, options.PackageSignTimestampUrl);

        if (options.SignOnMissingTool.HasValue)
            request.SignOnMissingTool = options.SignOnMissingTool;
        if (options.SignOnFailure.HasValue)
            request.SignOnFailure = options.SignOnFailure;

        if (options.WorkspaceEnableFeatures.Length > 0)
            request.WorkspaceEnableFeatures = options.WorkspaceEnableFeatures;
        if (options.WorkspaceDisableFeatures.Length > 0)
            request.WorkspaceDisableFeatures = options.WorkspaceDisableFeatures;
        if (options.Targets.Length > 0)
            request.Targets = options.Targets;
        if (options.Runtimes.Length > 0)
            request.Runtimes = options.Runtimes;
        if (options.Frameworks.Length > 0)
            request.Frameworks = options.Frameworks;
        if (options.Styles.Length > 0)
            request.Styles = options.Styles;
        if (options.Flavors.Length > 0)
            request.Flavors = options.Flavors;
        if (options.ToolOutputs.Length > 0)
            request.ToolOutputs = options.ToolOutputs;
        if (options.SkipToolOutputs.Length > 0)
            request.SkipToolOutputs = options.SkipToolOutputs;
        if (options.InstallerMsBuildProperties.Count > 0)
            request.InstallerMsBuildProperties = options.InstallerMsBuildProperties;

        return request;
    }

    private static PowerForgeReleaseRequest Clone(PowerForgeReleaseRequest? source)
    {
        if (source is null)
            return new PowerForgeReleaseRequest();

        return new PowerForgeReleaseRequest
        {
            ConfigPath = source.ConfigPath,
            PlanOnly = source.PlanOnly,
            ValidateOnly = source.ValidateOnly,
            PackagesOnly = source.PackagesOnly,
            ModuleOnly = source.ModuleOnly,
            ToolsOnly = source.ToolsOnly,
            PublishNuget = source.PublishNuget,
            PublishProjectGitHub = source.PublishProjectGitHub,
            PublishToolGitHub = source.PublishToolGitHub,
            Configuration = source.Configuration,
            ModuleNoDotnetBuild = source.ModuleNoDotnetBuild,
            ModuleVersion = source.ModuleVersion,
            ModulePreReleaseTag = source.ModulePreReleaseTag,
            ModuleNoSign = source.ModuleNoSign,
            ModuleSignModule = source.ModuleSignModule,
            SkipRestore = source.SkipRestore,
            SkipBuild = source.SkipBuild,
            SkipWorkspaceValidation = source.SkipWorkspaceValidation,
            WorkspaceConfigPath = source.WorkspaceConfigPath,
            WorkspaceProfile = source.WorkspaceProfile,
            WorkspaceTestimoXRoot = source.WorkspaceTestimoXRoot,
            WorkspaceEnableFeatures = source.WorkspaceEnableFeatures.ToArray(),
            WorkspaceDisableFeatures = source.WorkspaceDisableFeatures.ToArray(),
            OutputRoot = source.OutputRoot,
            StageRoot = source.StageRoot,
            ManifestJsonPath = source.ManifestJsonPath,
            AllowOutputOutsideProjectRoot = source.AllowOutputOutsideProjectRoot,
            AllowManifestOutsideProjectRoot = source.AllowManifestOutsideProjectRoot,
            ChecksumsPath = source.ChecksumsPath,
            SkipReleaseChecksums = source.SkipReleaseChecksums,
            KeepSymbols = source.KeepSymbols,
            EnableSigning = source.EnableSigning,
            SignProfile = source.SignProfile,
            SignToolPath = source.SignToolPath,
            SignThumbprint = source.SignThumbprint,
            SignSubjectName = source.SignSubjectName,
            SignOnMissingTool = source.SignOnMissingTool,
            SignOnFailure = source.SignOnFailure,
            SignTimestampUrl = source.SignTimestampUrl,
            SignDescription = source.SignDescription,
            SignUrl = source.SignUrl,
            SignCsp = source.SignCsp,
            SignKeyContainer = source.SignKeyContainer,
            PackageSignThumbprint = source.PackageSignThumbprint,
            PackageSignStore = source.PackageSignStore,
            PackageSignTimestampUrl = source.PackageSignTimestampUrl,
            InstallerMsBuildProperties = new Dictionary<string, string>(source.InstallerMsBuildProperties, StringComparer.OrdinalIgnoreCase),
            Targets = source.Targets.ToArray(),
            Runtimes = source.Runtimes.ToArray(),
            Frameworks = source.Frameworks.ToArray(),
            Styles = source.Styles.ToArray(),
            Flavors = source.Flavors.ToArray(),
            ToolOutputs = source.ToolOutputs.ToArray(),
            SkipToolOutputs = source.SkipToolOutputs.ToArray()
        };
    }

    private static string? ChooseString(string? currentValue, string? overrideValue)
        => string.IsNullOrWhiteSpace(overrideValue) ? currentValue : overrideValue;

    private static bool? ChooseBool(bool? currentValue, bool? overrideValue)
        => overrideValue.HasValue ? overrideValue : currentValue;
}
