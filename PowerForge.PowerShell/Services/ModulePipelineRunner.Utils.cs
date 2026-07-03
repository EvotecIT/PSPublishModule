using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void DeleteDirectoryWithRetries(string? path, int maxAttempts = 15, int initialDelayMs = 250)
    {
        if (path is null) return;
        var trimmed = path.Trim().Trim('"');
        if (trimmed.Length == 0) return;

        var full = Path.GetFullPath(trimmed);
        if (!Directory.Exists(full)) return;

        maxAttempts = Math.Max(1, maxAttempts);
        initialDelayMs = Math.Max(0, initialDelayMs);

        Exception? last = null;
        var delayMs = initialDelayMs;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                Directory.Delete(full, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
            }
            catch (UnauthorizedAccessException ex)
            {
                last = ex;
            }

            if (attempt >= maxAttempts)
                throw last ?? new IOException("Failed to delete directory.");

            // If the directory contains assemblies loaded in a collectible ALC, forcing GC helps release file locks.
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            catch
            {
                // best effort only
            }

            if (delayMs > 0)
                System.Threading.Thread.Sleep(delayMs);

            delayMs = delayMs <= 0 ? 0 : Math.Min(delayMs * 2, 5000);
        }
    }

    private void SyncGeneratedDocumentationToProjectRoot(ModulePipelinePlan plan, DocumentationBuildResult documentationResult)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (documentationResult is null) throw new ArgumentNullException(nameof(documentationResult));
        if (!documentationResult.Succeeded) return;
        if (plan.Documentation is null) return;
        if (plan.DocumentationBuild is null) return;

        var projectRoot = Path.GetFullPath(plan.ProjectRoot);

        var sourceDocs = documentationResult.DocsPath;
        if (string.IsNullOrWhiteSpace(sourceDocs) || !Directory.Exists(sourceDocs))
        {
            _logger.Warn("Documentation generation succeeded, but DocsPath does not exist; skipping project doc sync.");
            return;
        }

        var targetDocs = ResolvePath(projectRoot, plan.Documentation.Path);     
        var targetReadme = ResolvePath(projectRoot, plan.Documentation.PathReadme, optional: true);

        var fullTargetDocs = Path.GetFullPath(targetDocs).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullProjectRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(fullTargetDocs, fullProjectRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Documentation.Path resolves to the project root. Refusing to sync documentation to avoid overwriting project files. Set Documentation.Path to a folder (e.g. 'Docs').");

        var fullSourceDocs = Path.GetFullPath(sourceDocs).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!SamePath(fullSourceDocs, fullTargetDocs))
        {
            PruneStaleGeneratedMarkdown(sourceDocs, fullTargetDocs);
            DirectoryCopy(sourceDocs, fullTargetDocs);
        }

        if (!string.IsNullOrWhiteSpace(documentationResult.ReadmePath) &&
            File.Exists(documentationResult.ReadmePath) &&
            !string.IsNullOrWhiteSpace(targetReadme))
        {
            var destDir = Path.GetDirectoryName(Path.GetFullPath(targetReadme));
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);
            if (!SamePath(documentationResult.ReadmePath, targetReadme))
                File.Copy(documentationResult.ReadmePath, targetReadme, overwrite: true);
        }

        if (plan.DocumentationBuild.GenerateExternalHelp &&
            plan.DocumentationBuild.SyncExternalHelpToProjectRoot &&
            !string.IsNullOrWhiteSpace(documentationResult.ExternalHelpFilePath) &&
            File.Exists(documentationResult.ExternalHelpFilePath))        
        {
            var externalHelpDir = Path.GetDirectoryName(Path.GetFullPath(documentationResult.ExternalHelpFilePath));
            var cultureFolder = string.IsNullOrWhiteSpace(externalHelpDir)
                ? plan.DocumentationBuild.ExternalHelpCulture
                : new DirectoryInfo(externalHelpDir).Name;

            var targetCultureDir = Path.Combine(projectRoot, cultureFolder);
            Directory.CreateDirectory(targetCultureDir);

            var targetHelpFile = Path.Combine(targetCultureDir, Path.GetFileName(documentationResult.ExternalHelpFilePath));
            if (!SamePath(documentationResult.ExternalHelpFilePath, targetHelpFile))
                File.Copy(documentationResult.ExternalHelpFilePath, targetHelpFile, overwrite: true);
        }

        _logger.Success($"Updated project documentation at '{targetDocs}'.");
    }

    private static DocumentationConfiguration NormalizeDocumentationForStaging(ModulePipelinePlan plan, string stagingPath)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (plan.Documentation is null) throw new ArgumentException("Plan does not contain documentation configuration.", nameof(plan));
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));

        return new DocumentationConfiguration
        {
            Path = NormalizeProjectPathForStaging(plan.ProjectRoot, plan.Documentation.Path, rejectProjectRoot: true),
            PathReadme = NormalizeProjectPathForStaging(plan.ProjectRoot, plan.Documentation.PathReadme, rejectProjectRoot: false)
        };
    }

    private static string NormalizeProjectPathForStaging(string projectRoot, string configuredPath, bool rejectProjectRoot)
    {
        var value = (configuredPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            if (rejectProjectRoot)
                throw new InvalidOperationException("Documentation.Path resolves to the project root. Refusing to build documentation to avoid overwriting project files. Set Documentation.Path to a folder (e.g. 'Docs').");

            return value;
        }

        try
        {
            var fullProjectRoot = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(fullProjectRoot, value))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rejectProjectRoot && SamePath(fullProjectRoot, fullPath))
                throw new InvalidOperationException("Documentation.Path resolves to the project root. Refusing to build documentation to avoid overwriting project files. Set Documentation.Path to a folder (e.g. 'Docs').");

            if (!Path.IsPathRooted(value) || !IsSameOrChildPath(fullProjectRoot, fullPath))
                return value;

            return GetRelativePathFromDirectory(fullProjectRoot, fullPath);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            return value;
        }
    }

    private static string GetRelativePathFromDirectory(string root, string path)
    {
        if (SamePath(root, path))
            return ".";

        var rootUri = new Uri(EnsureTrailingSeparator(root));
        var pathUri = new Uri(path);
        var relative = Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);

        return string.IsNullOrWhiteSpace(relative) ? "." : relative;
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
               path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private void PruneStaleGeneratedMarkdown(string sourceDocs, string targetDocs)
    {
        var source = Path.GetFullPath(sourceDocs).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.GetFullPath(targetDocs).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(target))
            return;

        var sourceFiles = new HashSet<string>(
            Directory.EnumerateFiles(source, "*.md", SearchOption.AllDirectories)
                .Select(file => GetRelativePath(source, file)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(target, "*.md", SearchOption.AllDirectories))
        {
            var relativePath = GetRelativePath(target, file);
            if (sourceFiles.Contains(relativePath))
                continue;
            if (!IsGeneratedDocumentationMarkdown(file))
                continue;

            try
            {
                File.Delete(file);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to remove stale generated documentation file '{file}'. Error: {ex.Message}");
            }
        }

        RemoveEmptyDirectories(target);
    }

    private static bool IsGeneratedDocumentationMarkdown(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;

            var text = ReadMarkdownHeader(path);
            return text.Contains("external help file:", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("generated: true", StringComparison.OrdinalIgnoreCase) ||
                   (text.Contains("Module Name:", StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("schema:", StringComparison.OrdinalIgnoreCase)) ||
                   (text.Contains("topic:", StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("schema: 1.0.0", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string ReadMarkdownHeader(string path)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[2048];
        var read = reader.Read(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    private static void RemoveEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(static path => path.Length))
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                continue;

            Directory.Delete(directory);
        }
    }

    private static bool SamePath(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    private static string GetRelativePath(string root, string file)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullFile = Path.GetFullPath(file);
        var prefix = fullRoot + Path.DirectorySeparatorChar;

        return fullFile.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullFile.Substring(prefix.Length)
            : Path.GetFileName(fullFile) ?? fullFile;
    }

    private static string ResolvePath(string baseDir, string path, bool optional = false)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return optional ? string.Empty : Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static void DirectoryCopy(string sourceDir, string destDir)
    {
        var source = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        Directory.CreateDirectory(dest);

        var sourcePrefix = source + Path.DirectorySeparatorChar;
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(dir);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
        }
    }
}
