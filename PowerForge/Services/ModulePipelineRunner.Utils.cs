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
    private static void DeleteDirectoryWithRetries(string? path, int maxAttempts = 10, int initialDelayMs = 250)
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

            delayMs = delayMs <= 0 ? 0 : Math.Min(delayMs * 2, 3000);
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

        if (plan.DocumentationBuild.StartClean)
        {
            try
            {
                if (Directory.Exists(fullTargetDocs))
                    Directory.Delete(fullTargetDocs, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to clean project docs folder '{fullTargetDocs}'. Error: {ex.Message}");
            }
        }

        DirectoryCopy(sourceDocs, fullTargetDocs);

        if (!string.IsNullOrWhiteSpace(documentationResult.ReadmePath) &&
            File.Exists(documentationResult.ReadmePath) &&
            !string.IsNullOrWhiteSpace(targetReadme))
        {
            var destDir = Path.GetDirectoryName(Path.GetFullPath(targetReadme));
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);
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
            File.Copy(documentationResult.ExternalHelpFilePath, targetHelpFile, overwrite: true);
        }

        _logger.Success($"Updated project documentation at '{targetDocs}'.");
    }

    private static string ResolvePath(string baseDir, string path, bool optional = false)
    {
        var p = (path ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(p)) return optional ? string.Empty : Path.GetFullPath(baseDir);
        if (Path.IsPathRooted(p)) return Path.GetFullPath(p);
        return Path.GetFullPath(Path.Combine(baseDir, p));
    }

    private static ConfigurationFormattingSegment? MergeFormattingSegments(
        ConfigurationFormattingSegment? existing,
        ConfigurationFormattingSegment incoming)
    {
        if (incoming is null) return existing;
        if (existing is null) return incoming;

        existing.Options ??= new FormattingOptions();
        incoming.Options ??= new FormattingOptions();

        existing.Options.UpdateProjectRoot |= incoming.Options.UpdateProjectRoot;

        MergeTarget(existing.Options.Standard, incoming.Options.Standard);
        MergeTarget(existing.Options.Merge, incoming.Options.Merge);

        return existing;

        static void MergeTarget(FormattingTargetOptions dst, FormattingTargetOptions src)
        {
            if (src is null) return;

            if (src.FormatCodePS1 is not null) dst.FormatCodePS1 = src.FormatCodePS1;
            if (src.FormatCodePSM1 is not null) dst.FormatCodePSM1 = src.FormatCodePSM1;
            if (src.FormatCodePSD1 is not null) dst.FormatCodePSD1 = src.FormatCodePSD1;

            if (src.Style?.PSD1 is not null)
            {
                dst.Style ??= new FormattingStyleOptions();
                dst.Style.PSD1 = src.Style.PSD1;
            }
        }
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

    private sealed class NullModulePipelineProgressReporter : IModulePipelineProgressReporter
    {
        public static readonly NullModulePipelineProgressReporter Instance = new();

        private NullModulePipelineProgressReporter() { }

        public void StepStarting(ModulePipelineStep step) { }
        public void StepCompleted(ModulePipelineStep step) { }
        public void StepFailed(ModulePipelineStep step, Exception error) { }
    }

}
