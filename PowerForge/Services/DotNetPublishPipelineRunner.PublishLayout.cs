using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void AppendPublishStyleArgs(List<string> args, DotNetPublishPublishOptions publish, DotNetPublishStyle style)
    {
        switch (style)
        {
            case DotNetPublishStyle.Portable:
            case DotNetPublishStyle.PortableCompat:
            case DotNetPublishStyle.PortableSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:PublishSingleFile=true");
                args.Add("/p:IncludeNativeLibrariesForSelfExtract=true");

                // Match TestimoX pattern: use project-controlled trimming via PortableTrim.
                var trim = style == DotNetPublishStyle.PortableSize;
                var trimMode = style == DotNetPublishStyle.PortableSize ? "full" : "partial";
                args.Add($"/p:PortableTrim={trim.ToString().ToLowerInvariant()}");
                args.Add($"/p:PortableTrimMode={trimMode}");

                if (publish.ReadyToRun.HasValue)
                    args.Add($"/p:PublishReadyToRun={publish.ReadyToRun.Value.ToString().ToLowerInvariant()}");

                break;
            }
            case DotNetPublishStyle.AotSpeed:
            case DotNetPublishStyle.AotSize:
            {
                args.Add("--self-contained");
                args.Add("true");
                args.Add("/p:NativeAotPublish=true");
                args.Add("/p:StripSymbols=true");
                args.Add($"/p:IlcOptimizationPreference={(style == DotNetPublishStyle.AotSize ? "Size" : "Speed")}");
                args.Add("/p:InvariantGlobalization=false");
                break;
            }
        }
    }

    private DotNetPublishCleanupResult ApplyCleanup(string publishDir, DotNetPublishPublishOptions publish)
    {
        int pdbRemoved = 0;
        int docsRemoved = 0;
        bool refPruned = false;

        try
        {
            var recurse = publish.Slim ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (!publish.KeepSymbols)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdb", recurse))
                    TryDeleteFile(file, ref pdbRemoved);
            }

            if (!publish.KeepDocs)
            {
                foreach (var file in EnumerateFilesSafe(publishDir, "*.xml", recurse))
                    TryDeleteFile(file, ref docsRemoved);
                foreach (var file in EnumerateFilesSafe(publishDir, "*.pdf", recurse))
                    TryDeleteFile(file, ref docsRemoved);
            }

            if (publish.PruneReferences)
            {
                var refDir = Path.Combine(publishDir, "ref");
                if (Directory.Exists(refDir))
                {
                    try
                    {
                        Directory.Delete(refDir, recursive: true);
                        refPruned = true;
                    }
                    catch { /* best effort */ }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Cleanup failed. Error: {ex.Message}");
        }

        return new DotNetPublishCleanupResult { PdbRemoved = pdbRemoved, DocsRemoved = docsRemoved, RefPruned = refPruned };
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern, SearchOption option)
    {
        try { return Directory.EnumerateFiles(root, pattern, option); }
        catch { return Array.Empty<string>(); }
    }

    private static void TryDeleteFile(string path, ref int counter)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            counter++;
        }
        catch { /* best effort */ }
    }

    private void TryRenameMainExecutable(string publishDir, string rid, string renameTo)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        var desired = renameTo;
        if (isWindowsRid && !desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired += ".exe";
        if (!isWindowsRid && desired.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            desired = Path.GetFileNameWithoutExtension(desired);

        var candidate = FindMainExecutable(publishDir, rid);
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate))
            return;

        var dest = Path.Combine(Path.GetDirectoryName(candidate)!, desired);
        if (string.Equals(candidate, dest, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(candidate, dest);
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to rename executable ({rid}). Error: {ex.Message}");
        }
    }

    private static string? FindMainExecutable(string root, string rid)
    {
        var isWindowsRid = rid.StartsWith("win-", StringComparison.OrdinalIgnoreCase);
        try
        {
            if (isWindowsRid)
            {
                var exes = Directory.EnumerateFiles(root, "*.exe", SearchOption.TopDirectoryOnly)
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.Length)
                    .ToArray();
                return exes.FirstOrDefault()?.FullName;
            }

            var files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Select(p => new FileInfo(p))
                .Where(f => string.IsNullOrWhiteSpace(f.Extension))
                .OrderByDescending(f => f.Length)
                .ToArray();
            return files.FirstOrDefault()?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static (int Files, long TotalBytes, string? ExePath, long? ExeBytes) SummarizeDirectory(string dir, string rid)
    {
        try
        {
            var all = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .ToArray();

            long total = 0;
            foreach (var f in all) total += f.Length;

            var exe = FindMainExecutable(dir, rid);
            long? exeBytes = null;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                exeBytes = new FileInfo(exe).Length;

            return (all.Length, total, exe, exeBytes);
        }
        catch
        {
            return (0, 0, null, null);
        }
    }

    private string? CreateZip(string outputDir, DotNetPublishPlan plan, DotNetPublishTargetPlan target, string rid, IReadOnlyDictionary<string, string> tokens)
    {
        try
        {
            var nameTemplate = string.IsNullOrWhiteSpace(target.Publish.ZipNameTemplate)
                ? "{target}-{framework}-{rid}-{style}.zip"
                : target.Publish.ZipNameTemplate!;

            var zipName = ApplyTemplate(nameTemplate, tokens);
            if (!zipName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                zipName += ".zip";

            var zipPath = string.IsNullOrWhiteSpace(target.Publish.ZipPath)
                ? Path.Combine(Path.GetDirectoryName(outputDir)!, zipName)
                : ResolvePath(plan.ProjectRoot, ApplyTemplate(target.Publish.ZipPath!, tokens));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            ZipFile.CreateFromDirectory(outputDir, zipPath);
            return zipPath;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to create zip for '{target.Name}' ({rid}). Error: {ex.Message}");
            return null;
        }
    }

}
