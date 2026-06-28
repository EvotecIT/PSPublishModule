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
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json"
        };

        var copiedFiles = new Dictionary<string, string>(CreateCurrentFileSystemPathComparer());
        var visibleConfigNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();
        foreach (var directory in GetGeneratedInstallerBuildConfigurationDirectories(projectRootPath, sourceProjectPath))
        {
            foreach (var file in Directory.EnumerateFiles(directory)
                .Where(file => candidates.Contains(Path.GetFileName(file))))
            {
                var targetPath = GetGeneratedInstallerVisibleBuildConfigurationTargetPath(
                    file,
                    workingDirectory,
                    visibleConfigNames);
                CopyGeneratedInstallerBuildFile(file, projectRootPath, workingDirectory, copiedFiles, queue, targetPath);
            }
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

    private static string GetGeneratedInstallerVisibleBuildConfigurationTargetPath(
        string sourcePath,
        string workingDirectory,
        ISet<string> visibleConfigNames)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (visibleConfigNames.Add(fileName))
        {
            return Path.Combine(workingDirectory, fileName);
        }

        return Path.Combine(
            workingDirectory,
            "PowerForgeInputs",
            "BuildConfig",
            fileName);
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
        IDictionary<string, string> copiedFiles,
        Queue<string> queue,
        string? targetPath = null)
    {
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var projectRootDirectory = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
        if (!sourceFullPath.StartsWith(projectRootDirectory, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourceFullPath) ||
            copiedFiles.ContainsKey(sourceFullPath))
        {
            return;
        }

        targetPath ??= GetGeneratedInstallerBuildFileTargetPath(sourceFullPath, projectRootDirectory, workingDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourceFullPath, targetPath, overwrite: true);
        copiedFiles[sourceFullPath] = targetPath;
        RebaseNuGetConfigPaths(targetPath, Path.GetDirectoryName(sourceFullPath)!);
        RewriteGeneratedInstallerBuildPropertyFunctionImports(targetPath, sourceFullPath, projectRootDirectory, workingDirectory, copiedFiles);
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
        foreach (var path in ResolveGeneratedInstallerGetPathOfFileAboveImport(expanded, sourceDirectory, projectRootDirectory))
        {
            yield return path;
        }

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
            var searchOption = SearchOption.TopDirectoryOnly;
            var recursiveIndex = importPath.IndexOf("**", StringComparison.Ordinal);
            var directory = Path.GetDirectoryName(importPath);
            var pattern = Path.GetFileName(importPath);
            if (recursiveIndex >= 0)
            {
                var prefix = importPath.Substring(0, recursiveIndex).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                directory = string.IsNullOrWhiteSpace(prefix)
                    ? sourceDirectory
                    : prefix;
                searchOption = SearchOption.AllDirectories;
            }
            if (string.IsNullOrWhiteSpace(directory) ||
                !Directory.Exists(directory))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(directory, pattern, searchOption))
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

    private static IEnumerable<string> ResolveGeneratedInstallerGetPathOfFileAboveImport(
        string import,
        string sourceDirectory,
        string projectRootDirectory)
    {
        const string marker = "[MSBuild]::GetPathOfFileAbove(";
        var markerIndex = import.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            yield break;
        }

        var startIndex = markerIndex + marker.Length;
        var endIndex = FindGeneratedInstallerBuildFunctionEnd(import, startIndex);
        if (endIndex < 0)
        {
            yield break;
        }

        var arguments = SplitGeneratedInstallerBuildFunctionArguments(import.Substring(startIndex, endIndex - startIndex));
        if (arguments.Count == 0)
        {
            yield break;
        }

        var fileName = TrimGeneratedInstallerBuildFunctionArgument(arguments[0]);
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
        {
            yield break;
        }

        var searchDirectory = sourceDirectory;
        if (arguments.Count > 1)
        {
            var startDirectory = TrimGeneratedInstallerBuildFunctionArgument(arguments[1]);
            if (!string.IsNullOrWhiteSpace(startDirectory) &&
                startDirectory.IndexOf("$(", StringComparison.Ordinal) < 0 &&
                startDirectory.IndexOf("%(", StringComparison.Ordinal) < 0)
            {
                searchDirectory = Path.IsPathRooted(startDirectory)
                    ? startDirectory
                    : Path.Combine(sourceDirectory, startDirectory);
            }
        }

        var projectRoot = AppendDirectorySeparator(Path.GetFullPath(projectRootDirectory));
        var currentDirectory = Path.GetFullPath(searchDirectory);
        while (AppendDirectorySeparator(currentDirectory).StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            var candidate = Path.Combine(currentDirectory, fileName);
            if (File.Exists(candidate))
            {
                yield return Path.GetFullPath(candidate);
                yield break;
            }

            var parent = Path.GetDirectoryName(currentDirectory);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(parent, currentDirectory, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            currentDirectory = parent;
        }
    }

    private static int FindGeneratedInstallerBuildFunctionEnd(string value, int startIndex)
    {
        var quote = '\0';
        for (var index = startIndex; index < value.Length; index++)
        {
            var current = value[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current == '\'' || current == '"')
            {
                quote = current;
                continue;
            }

            if (current == ')')
            {
                return index;
            }
        }

        return -1;
    }

    private static List<string> SplitGeneratedInstallerBuildFunctionArguments(string arguments)
    {
        var values = new List<string>();
        var quote = '\0';
        var startIndex = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var current = arguments[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (current == '\'' || current == '"')
            {
                quote = current;
                continue;
            }

            if (current == ',')
            {
                values.Add(arguments.Substring(startIndex, index - startIndex));
                startIndex = index + 1;
            }
        }

        values.Add(arguments.Substring(startIndex));
        return values;
    }

    private static string TrimGeneratedInstallerBuildFunctionArgument(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') ||
             (trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static void RewriteGeneratedInstallerBuildPropertyFunctionImports(
        string targetPath,
        string sourcePath,
        string projectRootDirectory,
        string workingDirectory,
        IDictionary<string, string> copiedFiles)
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

        var sourceDirectory = Path.GetDirectoryName(sourcePath)!;
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        var changed = false;
        foreach (var projectAttribute in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("Project"))
            .Where(attribute => attribute is not null))
        {
            var project = projectAttribute!.Value;
            if (project.IndexOf("[MSBuild]::GetPathOfFileAbove(", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var resolvedPath = ResolveGeneratedInstallerBuildImport(project, sourceDirectory, projectRootDirectory).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                continue;
            }

            var resolvedFullPath = Path.GetFullPath(resolvedPath);
            var resolvedTargetPath = copiedFiles.TryGetValue(resolvedFullPath, out var existingTarget)
                ? existingTarget
                : GetGeneratedInstallerBuildFileTargetPath(resolvedFullPath, projectRootDirectory, workingDirectory);
            projectAttribute.Value = GetRelativePathCompat(targetDirectory, resolvedTargetPath);
            changed = true;
        }

        if (changed)
        {
            document.Save(targetPath);
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
}
