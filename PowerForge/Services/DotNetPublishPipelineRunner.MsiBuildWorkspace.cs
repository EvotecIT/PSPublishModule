using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

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
        bool isGeneratedInstallerProject,
        string? payloadDirectoryOverride = null,
        string? harvestPathOverride = null)
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
        args.Add($"/p:PowerForgeMsiPayloadDir={payloadDirectoryOverride ?? prepare.StagingDir}");
        if (!string.IsNullOrWhiteSpace(prepare.ManifestPath))
        {
            args.Add($"/p:PowerForgeMsiPrepareManifest={prepare.ManifestPath}");
        }
        args.Add($"/p:PowerForgeMsiSourceTarget={prepare.Target}");
        args.Add($"/p:PowerForgeMsiSourceFramework={prepare.Framework}");
        args.Add($"/p:PowerForgeMsiSourceRuntime={prepare.Runtime}");
        args.Add($"/p:PowerForgeMsiSourceStyle={prepare.Style}");
        var harvestPath = harvestPathOverride ?? prepare.HarvestPath;
        if (!string.IsNullOrWhiteSpace(harvestPath))
        {
            args.Add($"/p:PowerForgeMsiHarvestPath={harvestPath}");
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
        string sourceProjectPath,
        DotNetPublishMsiPrepareResult? prepare = null)
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

            var externalFiles = PrepareGeneratedInstallerExternalFiles(workingDirectory, projectPath, prepare);

            return new GeneratedInstallerBuildWorkspace(
                workingDirectory,
                projectPath,
                externalFiles.PayloadDirectory,
                externalFiles.HarvestPath);
        }
        catch
        {
            TryDeleteGeneratedInstallerBuildWorkspace(workingDirectory);
            throw;
        }
    }

    private static GeneratedInstallerExternalFiles PrepareGeneratedInstallerExternalFiles(
        string workingDirectory,
        string projectPath,
        DotNetPublishMsiPrepareResult? prepare)
    {
        if (prepare is null)
        {
            return new GeneratedInstallerExternalFiles(null, null);
        }

        var inputsDirectory = Path.Combine(workingDirectory, "PowerForgeInputs");
        string? payloadDirectory = null;
        if (!string.IsNullOrWhiteSpace(prepare.StagingDir) &&
            Directory.Exists(prepare.StagingDir))
        {
            payloadDirectory = Path.Combine(inputsDirectory, "Payload");
            DirectoryCopy(prepare.StagingDir, payloadDirectory);
        }

        string? harvestPath = null;
        if (!string.IsNullOrWhiteSpace(prepare.HarvestPath) &&
            File.Exists(prepare.HarvestPath))
        {
            var prepareHarvestPath = prepare.HarvestPath!;
            Directory.CreateDirectory(inputsDirectory);
            harvestPath = Path.Combine(inputsDirectory, Path.GetFileName(prepareHarvestPath));
            File.Copy(prepareHarvestPath, harvestPath, overwrite: true);

            if (!string.IsNullOrWhiteSpace(payloadDirectory))
            {
                RewriteHarvestSourcePaths(harvestPath, prepare.StagingDir, payloadDirectory!);
            }

            RewriteWixProjectSourceIncludes(projectPath, prepareHarvestPath, harvestPath);
        }

        return new GeneratedInstallerExternalFiles(payloadDirectory, harvestPath);
    }

    private static void RewriteWixProjectSourceIncludes(
        string projectPath,
        string sourcePath,
        string targetPath)
    {
        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var targetRelativePath = GetRelativePathCompat(projectDirectory, targetPath);
        var changed = false;

        foreach (var include in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Compile", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Include"))
            .Where(attribute => attribute is not null))
        {
            var value = include!.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var includePath = Path.IsPathRooted(value)
                ? Path.GetFullPath(value)
                : Path.GetFullPath(Path.Combine(projectDirectory, value));
            if (!string.Equals(includePath, sourceFullPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            include.Value = targetRelativePath;
            changed = true;
        }

        if (changed)
        {
            document.Save(projectPath);
        }
    }

    private static void RewriteHarvestSourcePaths(
        string harvestPath,
        string sourcePayloadDirectory,
        string targetPayloadDirectory)
    {
        var document = XDocument.Load(harvestPath, LoadOptions.PreserveWhitespace);
        var sourceDirectory = AppendDirectorySeparator(Path.GetFullPath(sourcePayloadDirectory));
        var targetDirectory = Path.GetFullPath(targetPayloadDirectory);
        var changed = false;

        foreach (var sourceAttribute in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "File", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Source"))
            .Where(attribute => attribute is not null))
        {
            var value = sourceAttribute!.Value;
            if (string.IsNullOrWhiteSpace(value) ||
                !Path.IsPathRooted(value))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(value);
            if (!fullPath.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = fullPath.Substring(sourceDirectory.Length);
            sourceAttribute.Value = Path.Combine(targetDirectory, relativePath);
            changed = true;
        }

        if (changed)
        {
            document.Save(harvestPath);
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
        public GeneratedInstallerBuildWorkspace(
            string workingDirectory,
            string projectPath,
            string? payloadDirectory,
            string? harvestPath)
        {
            WorkingDirectory = workingDirectory;
            ProjectPath = projectPath;
            PayloadDirectory = payloadDirectory;
            HarvestPath = harvestPath;
        }

        public string WorkingDirectory { get; }

        public string ProjectPath { get; }

        public string? PayloadDirectory { get; }

        public string? HarvestPath { get; }

        public void Dispose()
        {
            TryDeleteGeneratedInstallerBuildWorkspace(WorkingDirectory);
        }
    }

    private sealed class GeneratedInstallerExternalFiles
    {
        public GeneratedInstallerExternalFiles(string? payloadDirectory, string? harvestPath)
        {
            PayloadDirectory = payloadDirectory;
            HarvestPath = harvestPath;
        }

        public string? PayloadDirectory { get; }

        public string? HarvestPath { get; }
    }
}
