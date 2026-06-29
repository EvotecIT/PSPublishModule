using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void CopyGeneratedInstallerBuildConfiguration(
        string workingDirectory,
        string? projectRoot,
        string sourceProjectPath)
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
            "Directory.Build.rsp",
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json"
        };

        var pathComparer = CreateCurrentFileSystemPathComparer();
        var copiedFiles = new Dictionary<string, string>(pathComparer);
        var plannedTargets = new Dictionary<string, string>(pathComparer);
        var plannedSources = new List<string>();
        var nuGetConfigs = new List<string>();
        var visibleConfigNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRootPath));
        var sourceProjectFullPath = Path.GetFullPath(sourceProjectPath);
        var sourceProjectDirectory = Path.GetDirectoryName(sourceProjectFullPath)!;
        var queue = new Queue<string>();
        foreach (var directory in GetGeneratedInstallerBuildConfigurationDirectories(projectRootPath, sourceProjectPath))
        {
            foreach (var file in Directory.EnumerateFiles(directory)
                .Where(file => candidates.Contains(Path.GetFileName(file))))
            {
                if (string.Equals(Path.GetFileName(file), "NuGet.config", StringComparison.OrdinalIgnoreCase))
                {
                    nuGetConfigs.Add(file);
                    continue;
                }

                var sourceFullPath = Path.GetFullPath(file);
                var targetPath = GetGeneratedInstallerVisibleBuildConfigurationTargetPath(
                    file,
                    workingDirectory,
                    projectRootDirectory,
                    visibleConfigNames);
                if (!plannedTargets.ContainsKey(sourceFullPath))
                {
                    plannedTargets[sourceFullPath] = targetPath;
                    plannedSources.Add(sourceFullPath);
                }
            }
        }

        foreach (var source in plannedSources)
        {
            CopyGeneratedInstallerBuildFile(source, projectRootPath, workingDirectory, sourceProjectDirectory, sourceProjectFullPath, copiedFiles, queue, plannedTargets);
        }

        CopyGeneratedInstallerNuGetConfiguration(nuGetConfigs, workingDirectory);

        while (queue.Count > 0)
        {
            var copiedSource = queue.Dequeue();
            foreach (var importPath in ResolveGeneratedInstallerBuildImports(copiedSource, projectRootPath, sourceProjectDirectory, sourceProjectFullPath))
            {
                CopyGeneratedInstallerBuildFile(importPath, projectRootPath, workingDirectory, sourceProjectDirectory, sourceProjectFullPath, copiedFiles, queue, plannedTargets);
            }
        }
    }

    private static string GetGeneratedInstallerVisibleBuildConfigurationTargetPath(
        string sourcePath,
        string workingDirectory,
        string projectRootDirectory,
        ISet<string> visibleConfigNames)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (visibleConfigNames.Add(fileName))
        {
            return Path.Combine(workingDirectory, fileName);
        }

        var relativePath = Path.GetFullPath(sourcePath)
            .Substring(AppendDirectorySeparator(Path.GetFullPath(projectRootDirectory)).Length);
        return Path.Combine(
            workingDirectory,
            "PowerForgeInputs",
            "BuildConfig",
            relativePath);
    }

    private static IEnumerable<string> GetGeneratedInstallerBuildConfigurationDirectories(
        string projectRoot,
        string sourceProjectPath)
    {
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceProjectPath));
        while (!string.IsNullOrWhiteSpace(sourceDirectory))
        {
            var fullDirectory = Path.GetFullPath(sourceDirectory);
            if (!AppendDirectorySeparator(fullDirectory).StartsWith(projectRootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            yield return fullDirectory;
            if (string.Equals(AppendDirectorySeparator(fullDirectory), projectRootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            sourceDirectory = Path.GetDirectoryName(fullDirectory);
        }
    }

    private static void CopyGeneratedInstallerBuildFile(
        string sourcePath,
        string projectRoot,
        string workingDirectory,
        string sourceProjectDirectory,
        string sourceProjectFullPath,
        IDictionary<string, string> copiedFiles,
        Queue<string> queue,
        IReadOnlyDictionary<string, string> plannedTargets)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
        if (!sourceFullPath.StartsWith(projectRootDirectory, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourceFullPath) ||
            copiedFiles.ContainsKey(sourceFullPath))
        {
            return;
        }

        var targetPath = plannedTargets.TryGetValue(sourceFullPath, out var plannedTarget)
            ? plannedTarget
            : GetGeneratedInstallerBuildFileTargetPath(sourceFullPath, projectRootDirectory, workingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourceFullPath, targetPath, overwrite: true);
        copiedFiles[sourceFullPath] = targetPath;
        RebaseNuGetConfigPaths(targetPath, Path.GetDirectoryName(sourceFullPath)!);
        RewriteGeneratedInstallerBuildFileDirectoryProperties(targetPath, sourceFullPath);
        RewriteGeneratedInstallerBuildImports(targetPath, sourceFullPath, projectRootDirectory, workingDirectory, sourceProjectDirectory, sourceProjectFullPath, copiedFiles, plannedTargets);
        queue.Enqueue(sourceFullPath);
    }

    private static string GetGeneratedInstallerBuildFileTargetPath(
        string sourceFullPath,
        string projectRootDirectory,
        string workingDirectory)
    {
        var relativePath = sourceFullPath.Substring(AppendDirectorySeparator(Path.GetFullPath(projectRootDirectory)).Length);
        return Path.Combine(workingDirectory, relativePath);
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

        if (RebaseNuGetConfigPaths(document, sourceDirectory))
        {
            document.Save(targetPath);
        }
    }

    private static bool RebaseNuGetConfigPaths(XDocument document, string sourceDirectory)
    {
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

        return changed;
    }

    private static void CopyGeneratedInstallerNuGetConfiguration(
        IEnumerable<string> sourcePaths,
        string workingDirectory)
    {
        var orderedSources = sourcePaths
            .Select(Path.GetFullPath)
            .Distinct(CreateCurrentFileSystemPathComparer())
            .Reverse()
            .ToArray();
        if (orderedSources.Length == 0)
        {
            return;
        }

        var merged = new XDocument(new XElement("configuration"));
        foreach (var sourcePath in orderedSources)
        {
            XDocument sourceDocument;
            try
            {
                sourceDocument = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            RebaseNuGetConfigPaths(sourceDocument, Path.GetDirectoryName(sourcePath)!);
            MergeNuGetConfigDocument(merged, sourceDocument);
        }

        if (merged.Root is null ||
            !merged.Root.HasElements)
        {
            return;
        }

        var targetPath = Path.Combine(workingDirectory, "NuGet.config");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        merged.Save(targetPath);
    }

    private static void MergeNuGetConfigDocument(XDocument target, XDocument source)
    {
        if (target.Root is null ||
            source.Root is null)
        {
            return;
        }

        foreach (var sourceSection in source.Root.Elements())
        {
            var targetSection = target.Root.Elements()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, sourceSection.Name.LocalName, StringComparison.OrdinalIgnoreCase));
            if (targetSection is null)
            {
                target.Root.Add(new XElement(sourceSection));
                continue;
            }

            foreach (var sourceChild in sourceSection.Elements())
            {
                if (string.Equals(sourceChild.Name.LocalName, "clear", StringComparison.OrdinalIgnoreCase))
                {
                    targetSection.RemoveNodes();
                    targetSection.Add(new XElement(sourceChild));
                    continue;
                }

                var key = (string?)sourceChild.Attribute("key");
                if (!string.IsNullOrWhiteSpace(key))
                {
                    targetSection.Elements()
                        .Where(element =>
                            string.Equals(element.Name.LocalName, sourceChild.Name.LocalName, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals((string?)element.Attribute("key"), key, StringComparison.OrdinalIgnoreCase))
                        .Remove();
                }
                else
                {
                    targetSection.Elements()
                        .Where(element => string.Equals(element.Name.LocalName, sourceChild.Name.LocalName, StringComparison.OrdinalIgnoreCase))
                        .Remove();
                }

                targetSection.Add(new XElement(sourceChild));
            }
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

    private static void RewriteGeneratedInstallerBuildFileDirectoryProperties(
        string targetPath,
        string sourcePath)
    {
        XDocument document;
        try
        {
            document = XDocument.Load(targetPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return;
        }

        var sourceDirectory = EnsureTrailingDirectorySeparator(Path.GetDirectoryName(sourcePath)!);
        var changed = false;
        foreach (var element in document.Descendants()
            .Where(element => !string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                if (attribute.Value.IndexOf("$(MSBuildThisFileDirectory)", StringComparison.Ordinal) >= 0)
                {
                    attribute.Value = attribute.Value.Replace("$(MSBuildThisFileDirectory)", sourceDirectory);
                    changed = true;
                }
            }

            if (!element.HasElements &&
                element.Value.IndexOf("$(MSBuildThisFileDirectory)", StringComparison.Ordinal) >= 0)
            {
                element.Value = element.Value.Replace("$(MSBuildThisFileDirectory)", sourceDirectory);
                changed = true;
            }
        }

        if (changed)
        {
            document.Save(targetPath);
        }
    }

    private static string ExpandGeneratedInstallerBuildImport(
        string import,
        string sourceDirectory,
        string projectRootDirectory,
        string sourceProjectDirectory,
        string sourceProjectFullPath)
    {
        return import
            .Replace("$(MSBuildThisFileDirectory)", EnsureTrailingDirectorySeparator(sourceDirectory))
            .Replace("$(MSBuildProjectDirectory)", sourceProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .Replace("$(MSBuildProjectFullPath)", sourceProjectFullPath)
            .Replace("$(MSBuildProjectExtension)", ".wixproj");
    }

    private static string NormalizeRelativePathSeparators(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }
}
