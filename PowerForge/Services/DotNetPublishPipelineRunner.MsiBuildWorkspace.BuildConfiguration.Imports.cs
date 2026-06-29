using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static IEnumerable<string> ResolveGeneratedInstallerBuildImports(
        string sourcePath,
        string projectRoot,
        string sourceProjectDirectory,
        string sourceProjectFullPath)
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
            foreach (var path in ResolveGeneratedInstallerBuildImport(import, sourceDirectory, projectRootDirectory, sourceProjectDirectory, sourceProjectFullPath))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> ResolveGeneratedInstallerBuildImport(
        string import,
        string sourceDirectory,
        string projectRootDirectory,
        string sourceProjectDirectory,
        string sourceProjectFullPath)
    {
        var expanded = NormalizeRelativePathSeparators(ExpandGeneratedInstallerBuildImport(import, sourceDirectory, projectRootDirectory, sourceProjectDirectory, sourceProjectFullPath));
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
        var nestedPropertyDepth = 0;
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

            if (current == '$' &&
                index + 1 < value.Length &&
                value[index + 1] == '(')
            {
                nestedPropertyDepth++;
                index++;
                continue;
            }

            if (current == '\'' || current == '"')
            {
                quote = current;
                continue;
            }

            if (current == ')')
            {
                if (nestedPropertyDepth > 0)
                {
                    nestedPropertyDepth--;
                    continue;
                }

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

    private static void RewriteGeneratedInstallerBuildImports(
        string targetPath,
        string sourcePath,
        string projectRootDirectory,
        string workingDirectory,
        string sourceProjectDirectory,
        string sourceProjectFullPath,
        IDictionary<string, string> copiedFiles,
        IReadOnlyDictionary<string, string> plannedTargets)
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
            if (project.IndexOfAny(new[] { '*', '?' }) >= 0)
            {
                if (TryRewriteGeneratedInstallerBuildWildcardImport(
                    projectAttribute,
                    project,
                    sourceDirectory,
                    targetDirectory,
                    projectRootDirectory,
                    workingDirectory,
                    sourceProjectDirectory,
                    sourceProjectFullPath))
                {
                    changed = true;
                }

                continue;
            }

            var resolvedPaths = ResolveGeneratedInstallerBuildImport(project, sourceDirectory, projectRootDirectory, sourceProjectDirectory, sourceProjectFullPath)
                .Take(2)
                .ToArray();
            if (resolvedPaths.Length != 1)
            {
                continue;
            }

            var resolvedFullPath = Path.GetFullPath(resolvedPaths[0]);
            var resolvedTargetPath = copiedFiles.TryGetValue(resolvedFullPath, out var existingTarget)
                ? existingTarget
                : plannedTargets.TryGetValue(resolvedFullPath, out var plannedTarget)
                    ? plannedTarget
                : GetGeneratedInstallerBuildFileTargetPath(resolvedFullPath, projectRootDirectory, workingDirectory);
            var rewrittenProject = GetRelativePathCompat(targetDirectory, resolvedTargetPath);
            projectAttribute.Value = rewrittenProject;
            var condition = projectAttribute.Parent?.Attribute("Condition");
            if (condition is not null)
            {
                condition.Value = RewriteGeneratedInstallerBuildImportCondition(condition.Value, project, rewrittenProject);
            }

            changed = true;
        }

        if (changed)
        {
            document.Save(targetPath);
        }
    }

    private static bool TryRewriteGeneratedInstallerBuildWildcardImport(
        XAttribute projectAttribute,
        string project,
        string sourceDirectory,
        string targetDirectory,
        string projectRootDirectory,
        string workingDirectory,
        string sourceProjectDirectory,
        string sourceProjectFullPath)
    {
        var expanded = NormalizeRelativePathSeparators(ExpandGeneratedInstallerBuildImport(project, sourceDirectory, projectRootDirectory, sourceProjectDirectory, sourceProjectFullPath));
        if (expanded.IndexOf("$(", StringComparison.Ordinal) >= 0 ||
            expanded.IndexOf("%(", StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        var importPath = Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(sourceDirectory, expanded);
        var fullImportPath = Path.GetFullPath(importPath);
        var projectRoot = AppendDirectorySeparator(Path.GetFullPath(projectRootDirectory));
        if (!fullImportPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rewrittenTarget = GetGeneratedInstallerBuildFileTargetPath(fullImportPath, projectRootDirectory, workingDirectory);
        var rewrittenProject = GetRelativePathCompat(targetDirectory, rewrittenTarget);
        projectAttribute.Value = rewrittenProject;
        var condition = projectAttribute.Parent?.Attribute("Condition");
        if (condition is not null)
        {
            condition.Value = RewriteGeneratedInstallerBuildImportCondition(condition.Value, project, rewrittenProject);
        }

        return true;
    }

    private static string RewriteGeneratedInstallerBuildImportCondition(
        string condition,
        string originalProject,
        string rewrittenProject)
    {
        var rewrittenCondition = ReplaceGeneratedInstallerBuildPropertyFunctionExpressions(condition, rewrittenProject);
        if (!string.Equals(rewrittenCondition, condition, StringComparison.Ordinal))
        {
            return rewrittenCondition;
        }

        if (!string.IsNullOrWhiteSpace(originalProject) &&
            rewrittenCondition.IndexOf(originalProject, StringComparison.Ordinal) >= 0)
        {
            return rewrittenCondition.Replace(originalProject, rewrittenProject);
        }

        return rewrittenCondition;
    }

    private static string ReplaceGeneratedInstallerBuildPropertyFunctionExpressions(
        string value,
        string replacement)
    {
        const string marker = "$([MSBuild]::GetPathOfFileAbove(";
        var rewritten = value;
        var searchIndex = 0;
        while (searchIndex < rewritten.Length)
        {
            var markerIndex = rewritten.IndexOf(marker, searchIndex, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                break;
            }

            var argumentsStart = markerIndex + marker.Length;
            var functionEnd = FindGeneratedInstallerBuildFunctionEnd(rewritten, argumentsStart);
            if (functionEnd < 0)
            {
                break;
            }

            var expressionEnd = functionEnd + 1;
            if (expressionEnd < rewritten.Length &&
                rewritten[expressionEnd] == ')')
            {
                expressionEnd++;
            }

            rewritten = rewritten.Substring(0, markerIndex) +
                replacement +
                rewritten.Substring(expressionEnd);
            searchIndex = markerIndex + replacement.Length;
        }

        return rewritten;
    }
}
