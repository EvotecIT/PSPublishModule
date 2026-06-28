using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static List<string> BuildMsiBuildArguments(
        DotNetPublishPlan plan,
        DotNetPublishStep step,
        DotNetPublishMsiPrepareResult prepare,
        string installerId,
        string installerProjectPath,
        string? configuredOutputDir,
        string? configuredOutputName,
        IReadOnlyDictionary<string, string> installerMsBuildProperties,
        MsiVersionResolution versionResolution,
        MsiClientLicenseResolution licenseResolution,
        bool isGeneratedInstallerProject)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (step is null) throw new ArgumentNullException(nameof(step));
        if (prepare is null) throw new ArgumentNullException(nameof(prepare));
        if (string.IsNullOrWhiteSpace(installerId))
        {
            throw new ArgumentException("Installer id is required.", nameof(installerId));
        }
        if (string.IsNullOrWhiteSpace(installerProjectPath))
        {
            throw new ArgumentException("Installer project path is required.", nameof(installerProjectPath));
        }

        var args = new List<string>
        {
            "build",
            installerProjectPath,
            "-c",
            plan.Configuration,
            "--nologo"
        };

        if (plan.Restore && !isGeneratedInstallerProject)
        {
            args.Add("--no-restore");
        }

        args.AddRange(BuildMsBuildPropertyArgs(installerMsBuildProperties));
        if (!string.IsNullOrWhiteSpace(configuredOutputDir))
        {
            args.Add($"/p:OutputPath={EnsureTrailingDirectorySeparator(configuredOutputDir!)}");
        }
        if (!string.IsNullOrWhiteSpace(configuredOutputName))
        {
            args.Add($"/p:OutputName={configuredOutputName}");
        }
        args.Add($"/p:PowerForgeMsiInstallerId={installerId}");
        args.Add($"/p:PowerForgeMsiPayloadDir={prepare.StagingDir}");
        if (!string.IsNullOrWhiteSpace(prepare.ManifestPath))
        {
            args.Add($"/p:PowerForgeMsiPrepareManifest={prepare.ManifestPath}");
        }
        args.Add($"/p:PowerForgeMsiSourceTarget={prepare.Target}");
        args.Add($"/p:PowerForgeMsiSourceFramework={prepare.Framework}");
        args.Add($"/p:PowerForgeMsiSourceRuntime={prepare.Runtime}");
        args.Add($"/p:PowerForgeMsiSourceStyle={prepare.Style}");
        if (!string.IsNullOrWhiteSpace(prepare.HarvestPath))
        {
            args.Add($"/p:PowerForgeMsiHarvestPath={prepare.HarvestPath}");
        }
        if (!string.IsNullOrWhiteSpace(prepare.HarvestDirectoryRefId))
        {
            args.Add($"/p:PowerForgeMsiHarvestDirectoryRefId={prepare.HarvestDirectoryRefId}");
        }
        if (!string.IsNullOrWhiteSpace(prepare.HarvestComponentGroupId))
        {
            args.Add($"/p:PowerForgeMsiHarvestComponentGroupId={prepare.HarvestComponentGroupId}");
        }
        if (!string.IsNullOrWhiteSpace(versionResolution.Version) &&
            !string.IsNullOrWhiteSpace(versionResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(versionResolution.PropertyName!))
        {
            args.Add($"/p:PowerForgeMsiVersion={versionResolution.Version}");
        }
        if (!string.IsNullOrWhiteSpace(licenseResolution.Path) &&
            !string.IsNullOrWhiteSpace(licenseResolution.PropertyName) &&
            !installerMsBuildProperties.ContainsKey(licenseResolution.PropertyName!))
        {
            args.Add($"/p:PowerForgeMsiClientLicensePath={licenseResolution.Path}");
            if (!string.IsNullOrWhiteSpace(licenseResolution.ClientId))
            {
                args.Add($"/p:PowerForgeMsiClientId={licenseResolution.ClientId}");
            }
        }

        return args;
    }

    internal static GeneratedInstallerBuildWorkspace PrepareGeneratedInstallerBuildWorkspace(
        string installerId,
        string sourceProjectDirectory,
        string sourceProjectPath)
    {
        if (string.IsNullOrWhiteSpace(installerId))
        {
            throw new ArgumentException("Installer id is required.", nameof(installerId));
        }
        if (string.IsNullOrWhiteSpace(sourceProjectDirectory))
        {
            throw new ArgumentException("Source project directory is required.", nameof(sourceProjectDirectory));
        }
        if (string.IsNullOrWhiteSpace(sourceProjectPath))
        {
            throw new ArgumentException("Source project path is required.", nameof(sourceProjectPath));
        }

        var sourceDirectory = Path.GetFullPath(sourceProjectDirectory);
        var sourcePath = Path.GetFullPath(sourceProjectPath);
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Generated WiX project directory not found: {sourceDirectory}");
        }
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException($"Generated WiX project file not found: {sourcePath}", sourcePath);
        }

        var safeInstallerId = SanitizeWixIdentifier(installerId, "Installer");
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "PowerForge",
            "WixBuild",
            $"{safeInstallerId}-{Guid.NewGuid():N}");

        try
        {
            DirectoryCopy(sourceDirectory, workingDirectory);
            var projectPath = Path.Combine(workingDirectory, Path.GetFileName(sourcePath));
            if (!File.Exists(projectPath))
            {
                throw new FileNotFoundException($"Temporary generated WiX project file not found: {projectPath}", projectPath);
            }

            return new GeneratedInstallerBuildWorkspace(workingDirectory, projectPath);
        }
        catch
        {
            TryDeleteGeneratedInstallerBuildWorkspace(workingDirectory);
            throw;
        }
    }

    private static void TryDeleteGeneratedInstallerBuildWorkspace(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(workingDirectory))
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup; the primary generated authoring remains in the configured artifact folder.
        }
    }

    internal sealed class GeneratedInstallerBuildWorkspace : IDisposable
    {
        public GeneratedInstallerBuildWorkspace(string workingDirectory, string projectPath)
        {
            WorkingDirectory = workingDirectory;
            ProjectPath = projectPath;
        }

        public string WorkingDirectory { get; }

        public string ProjectPath { get; }

        public void Dispose()
        {
            TryDeleteGeneratedInstallerBuildWorkspace(WorkingDirectory);
        }
    }
}
