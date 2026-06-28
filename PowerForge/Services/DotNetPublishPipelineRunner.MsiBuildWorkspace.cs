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
        DotNetPublishMsiPrepareResult? prepare = null,
        string? projectRoot = null)
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
            CopyGeneratedInstallerBuildConfiguration(workingDirectory, projectRoot);
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

    private static void CopyGeneratedInstallerBuildConfiguration(string workingDirectory, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            !Directory.Exists(projectRoot))
        {
            return;
        }
        var projectRootPath = projectRoot!;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NuGet.config",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json"
        };

        var copiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var file in Directory.EnumerateFiles(projectRootPath)
            .Where(file => candidates.Contains(Path.GetFileName(file))))
        {
            CopyGeneratedInstallerBuildFile(file, projectRootPath, workingDirectory, copiedFiles, queue);
        }

        while (queue.Count > 0)
        {
            var copiedSource = queue.Dequeue();
            foreach (var importPath in ResolveGeneratedInstallerBuildImports(copiedSource, projectRootPath))
            {
                CopyGeneratedInstallerBuildFile(importPath, projectRootPath, workingDirectory, copiedFiles, queue);
            }
        }
    }

    private static void CopyGeneratedInstallerBuildFile(
        string sourcePath,
        string projectRoot,
        string workingDirectory,
        ISet<string> copiedFiles,
        Queue<string> queue)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
        if (!sourceFullPath.StartsWith(projectRootDirectory, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourceFullPath) ||
            !copiedFiles.Add(sourceFullPath))
        {
            return;
        }

        var relativePath = sourceFullPath.Substring(projectRootDirectory.Length);
        var targetPath = Path.Combine(workingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourceFullPath, targetPath, overwrite: true);
        RebaseNuGetConfigPaths(targetPath, Path.GetDirectoryName(sourceFullPath)!);
        queue.Enqueue(sourceFullPath);
    }

    private static void RebaseNuGetConfigPaths(string targetPath, string sourceDirectory)
    {
        if (!string.Equals(Path.GetFileName(targetPath), "NuGet.config", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(targetPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return;
        }

        var changed = false;
        var pathAttributes = document
            .Descendants()
            .Where(IsNuGetPathElement)
            .Select(element => element.Attribute("value"))
            .Where(attribute => attribute is not null);
        foreach (var valueAttribute in pathAttributes)
        {
            var value = valueAttribute!.Value;
            if (!ShouldRebaseNuGetPath(value))
            {
                continue;
            }

            valueAttribute.Value = Path.GetFullPath(Path.Combine(sourceDirectory, NormalizeRelativePathSeparators(value)));
            changed = true;
        }

        if (changed)
        {
            document.Save(targetPath);
        }
    }

    private static bool IsNuGetPathElement(XElement element)
    {
        if (!string.Equals(element.Name.LocalName, "add", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parentName = element.Parent?.Name.LocalName;
        if (string.Equals(parentName, "packageSources", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parentName, "fallbackPackageFolders", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.Equals(parentName, "config", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = (string?)element.Attribute("key");
        return string.Equals(key, "globalPackagesFolder", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "repositoryPath", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, "http_cache_path", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldRebaseNuGetPath(string value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !IsUriPackageSource(value) &&
            !IsRootedPackageSource(value) &&
            value.IndexOf('$') < 0 &&
            value.IndexOf('%') < 0;
    }

    private static bool IsUriPackageSource(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    private static bool IsRootedPackageSource(string value)
    {
        return Path.IsPathRooted(value) ||
            (value.Length >= 3 &&
             char.IsLetter(value[0]) &&
             value[1] == ':' &&
             (value[2] == '\\' || value[2] == '/'));
    }

    private static IEnumerable<string> ResolveGeneratedInstallerBuildImports(string sourcePath, string projectRoot)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
        foreach (var import in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Project"))
            .Where(attribute => attribute is not null)
            .Select(attribute => attribute!.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            foreach (var path in ResolveGeneratedInstallerBuildImport(import, sourceDirectory, projectRootDirectory))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ResolveGeneratedInstallerBuildImport(
        string import,
        string sourceDirectory,
        string projectRootDirectory)
    {
        var expanded = NormalizeRelativePathSeparators(ExpandGeneratedInstallerBuildImport(import, sourceDirectory, projectRootDirectory));
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0)
        {
            yield break;
        }

        var importPath = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(sourceDirectory, expanded);
        var projectRoot = AppendDirectorySeparator(Path.GetFullPath(projectRootDirectory));
        if (importPath.IndexOfAny(new[] { '*', '?' }) >= 0)
        {
            var directory = Path.GetDirectoryName(importPath);
            var pattern = Path.GetFileName(importPath);
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
            {
                var fullPath = Path.GetFullPath(file);
                if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                {
                    yield return fullPath;
                }
            }

            yield break;
        }

        var fullImportPath = Path.GetFullPath(importPath);
        if (fullImportPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(fullImportPath))
        {
            yield return fullImportPath;
        }
    }

    private static string ExpandGeneratedInstallerBuildImport(
        string import,
        string sourceDirectory,
        string projectRootDirectory)
    {
        return import
            .Replace("$(MSBuildThisFileDirectory)", EnsureTrailingDirectorySeparator(sourceDirectory))
            .Replace("$(MSBuildProjectDirectory)", projectRootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Replace("$(MSBuildProjectFullPath)", Path.Combine(projectRootDirectory, "PowerForge.Generated.wixproj"))
            .Replace("$(MSBuildProjectExtension)", ".wixproj");
    }

    private static string NormalizeRelativePathSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
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
            RewriteWixProjectDefineConstants(
                projectPath,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PayloadDir"] = payloadDirectory,
                    ["PowerForgeMsiPayloadDir"] = payloadDirectory
                });
        }

        RewriteGeneratedInstallerAssetPaths(
            Path.Combine(workingDirectory, "Product.wxs"),
            inputsDirectory,
            prepare.StagingDir,
            payloadDirectory);

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

    private static void RewriteWixProjectDefineConstants(
        string projectPath,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        var document = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        var changed = false;

        foreach (var element in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "DefineConstants", StringComparison.OrdinalIgnoreCase)))
        {
            var entries = element.Value
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .ToArray();
            var rewrittenEntries = new List<string>(entries.Length);
            foreach (var entry in entries)
            {
                var separatorIndex = entry.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    rewrittenEntries.Add(entry);
                    continue;
                }

                var key = entry.Substring(0, separatorIndex).Trim();
                if (replacements.TryGetValue(key, out var replacement))
                {
                    rewrittenEntries.Add(key + "=" + replacement);
                    changed = true;
                    continue;
                }

                rewrittenEntries.Add(entry);
            }

            if (changed)
            {
                element.Value = string.Join(";", rewrittenEntries);
            }
        }

        if (changed)
        {
            document.Save(projectPath);
        }
    }

    private static void RewriteGeneratedInstallerAssetPaths(
        string sourcePath,
        string inputsDirectory,
        string? sourcePayloadDirectory,
        string? targetPayloadDirectory)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        var assetsDirectory = Path.Combine(inputsDirectory, "Assets");
        var copiedAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var valueAttribute in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "WixVariable", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Value"))
            .Where(attribute => attribute is not null))
        {
            if (TryRewriteGeneratedInstallerFileAttribute(
                valueAttribute!,
                sourceDirectory,
                assetsDirectory,
                copiedAssets,
                sourcePayloadDirectory,
                targetPayloadDirectory))
            {
                changed = true;
            }
        }

        foreach (var sourceAttribute in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "File", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Source"))
            .Where(attribute => attribute is not null))
        {
            if (TryRewriteGeneratedInstallerFileAttribute(
                sourceAttribute!,
                sourceDirectory,
                assetsDirectory,
                copiedAssets,
                sourcePayloadDirectory,
                targetPayloadDirectory))
            {
                changed = true;
            }
        }

        if (changed)
        {
            document.Save(sourcePath);
        }
    }

    private static bool TryRewriteGeneratedInstallerFileAttribute(
        XAttribute attribute,
        string sourceDirectory,
        string assetsDirectory,
        IDictionary<string, string> copiedAssets,
        string? sourcePayloadDirectory,
        string? targetPayloadDirectory)
    {
        var value = attribute.Value;
        if (string.IsNullOrWhiteSpace(value) ||
            !Path.IsPathRooted(value) ||
            !File.Exists(value))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(value);
        if (!string.IsNullOrWhiteSpace(sourcePayloadDirectory) &&
            !string.IsNullOrWhiteSpace(targetPayloadDirectory))
        {
            var sourcePayload = AppendDirectorySeparator(Path.GetFullPath(sourcePayloadDirectory!));
            if (fullPath.StartsWith(sourcePayload, StringComparison.OrdinalIgnoreCase))
            {
                var relativePayloadPath = fullPath.Substring(sourcePayload.Length);
                attribute.Value = GetRelativePathCompat(sourceDirectory, Path.Combine(targetPayloadDirectory!, relativePayloadPath));
                return true;
            }
        }

        if (!copiedAssets.TryGetValue(fullPath, out var copiedPath))
        {
            Directory.CreateDirectory(assetsDirectory);
            copiedPath = GetUniqueAssetPath(assetsDirectory, Path.GetFileName(fullPath));
            File.Copy(fullPath, copiedPath, overwrite: false);
            copiedAssets[fullPath] = copiedPath;
        }

        attribute.Value = GetRelativePathCompat(sourceDirectory, copiedPath);
        return true;
    }

    private static string GetUniqueAssetPath(string directory, string fileName)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName)
            ? "asset"
            : fileName;
        var candidate = Path.Combine(directory, safeName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(safeName);
        var extension = Path.GetExtension(safeName);
        for (var index = 1; ; index++)
        {
            candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
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
