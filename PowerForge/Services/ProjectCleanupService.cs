using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Removes specific files and folders from a project directory with safety features such as backups,
/// retries, and support for WhatIf / ShouldProcess style execution.
/// </summary>
public sealed class ProjectCleanupService
{
    /// <summary>
    /// Executes cleanup for the specified project according to <paramref name="spec"/>.
    /// </summary>
    /// <param name="spec">Cleanup specification.</param>
    /// <param name="shouldProcess">
    /// Optional callback used to emulate PowerShell <c>ShouldProcess</c>. When it returns false,
    /// the item is treated as <see cref="ProjectCleanupStatus.WhatIf"/>.
    /// </param>
    /// <param name="onItemProcessed">Optional callback invoked after each item is processed.</param>
    /// <exception cref="ArgumentException">Thrown when required values are missing.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the project directory does not exist.</exception>
    public ProjectCleanupOutput Clean(
        ProjectCleanupSpec spec,
        Func<string, string, bool>? shouldProcess = null,
        Action<int, int, ProjectCleanupItemResult>? onItemProcessed = null)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ProjectPath))
            throw new ArgumentException("ProjectPath is required.", nameof(spec));

        var root = Path.GetFullPath(spec.ProjectPath.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Project path '{root}' not found or is not a directory");

        var includePatterns = (spec.IncludePatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var isCustom = includePatterns.Length > 0;

        var patterns = isCustom
            ? CleanupPatterns.FromCustom(includePatterns)
            : CleanupPatterns.FromProjectType(spec.ProjectType);

        var excludePatterns = (spec.ExcludePatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var excludeDirs = (spec.ExcludeDirectories ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var items = CollectItems(root, patterns, excludePatterns, excludeDirs, recurse: spec.Recurse, maxDepth: spec.MaxDepth)
            .OrderByDescending(i => GetDepth(i.RelativePath))
            .ToList();

        if (items.Count == 0)
        {
            return new ProjectCleanupOutput
            {
                Summary = new ProjectCleanupSummary
                {
                    ProjectPath = root,
                    ProjectType = isCustom ? "Custom" : spec.ProjectType.ToString(),
                    TotalItems = 0,
                    Removed = 0,
                    Errors = 0,
                    SpaceFreed = 0,
                    SpaceFreedMB = 0,
                    BackupDirectory = spec.CreateBackups ? spec.BackupDirectory : null,
                    DeleteMethod = spec.DeleteMethod.ToString()
                },
                Results = Array.Empty<ProjectCleanupItemResult>()
            };
        }

        if (spec.CreateBackups)
        {
            if (string.IsNullOrWhiteSpace(spec.BackupDirectory))
            {
                spec.BackupDirectory = Path.Combine(Path.GetTempPath(), $"PowerForge_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            var backupDir = spec.BackupDirectory!;
            if (!Directory.Exists(backupDir))
                Directory.CreateDirectory(backupDir);

            CreateItemBackups(items, backupDir);
        }

        var totalItems = items.Count;
        var results = new List<ProjectCleanupItemResult>(totalItems);

        for (var idx = 0; idx < items.Count; idx++)
        {
            var item = items[idx];
            var result = new ProjectCleanupItemResult
            {
                RelativePath = item.RelativePath,
                FullPath = item.FullPath,
                Type = item.Type,
                Pattern = item.Pattern,
                Status = ProjectCleanupStatus.Error,
                Size = item.Size,
                Error = null
            };

            if (spec.WhatIf)
            {
                result.Status = ProjectCleanupStatus.WhatIf;
                results.Add(result);
                onItemProcessed?.Invoke(idx + 1, totalItems, result);
                continue;
            }

            try
            {
                var action = $"Remove ({spec.DeleteMethod})";
                if (shouldProcess is not null && !shouldProcess(item.FullPath, action))
                {
                    result.Status = ProjectCleanupStatus.WhatIf;
                    results.Add(result);
                    onItemProcessed?.Invoke(idx + 1, totalItems, result);
                    continue;
                }

                var ok = TryDeleteWithRetries(item.FullPath, item.Type == ProjectCleanupItemType.Folder, spec.DeleteMethod, spec.Retries, out var err);
                if (ok)
                {
                    result.Status = ProjectCleanupStatus.Removed;
                }
                else
                {
                    result.Status = ProjectCleanupStatus.Failed;
                    result.Error = err ?? "Delete returned false";
                }
            }
            catch (Exception ex)
            {
                result.Status = ProjectCleanupStatus.Error;
                result.Error = ex.Message;
            }

            results.Add(result);
            onItemProcessed?.Invoke(idx + 1, totalItems, result);
        }

        var removed = results.Count(x => x.Status == ProjectCleanupStatus.Removed);
        var errors = results.Count(x => x.Status == ProjectCleanupStatus.Error);
        var freed = results.Where(x => x.Status == ProjectCleanupStatus.Removed).Sum(x => x.Size);
        var freedMb = Math.Round(freed / (1024d * 1024d), 2);

        return new ProjectCleanupOutput
        {
            Summary = new ProjectCleanupSummary
            {
                ProjectPath = root,
                ProjectType = isCustom ? "Custom" : spec.ProjectType.ToString(),
                TotalItems = totalItems,
                Removed = removed,
                Errors = errors,
                SpaceFreed = freed,
                SpaceFreedMB = freedMb,
                BackupDirectory = spec.CreateBackups ? spec.BackupDirectory : null,
                DeleteMethod = spec.DeleteMethod.ToString()
            },
            Results = results.ToArray()
        };
    }

    private static bool TryDeleteWithRetries(string fullPath, bool isDirectory, ProjectDeleteMethod method, int retries, out string? error)
    {
        error = null;
        var attempts = Math.Max(1, retries);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                switch (method)
                {
                    case ProjectDeleteMethod.RemoveItem:
                        if (isDirectory)
                        {
                            Directory.Delete(fullPath, recursive: true);
                        }
                        else
                        {
                            File.SetAttributes(fullPath, FileAttributes.Normal);
                            File.Delete(fullPath);
                        }
                        return true;
                    case ProjectDeleteMethod.DotNetDelete:
                        if (isDirectory)
                        {
                            new DirectoryInfo(fullPath).Delete(recursive: true);
                        }
                        else
                        {
                            new FileInfo(fullPath).Delete();
                        }
                        return true;
                    case ProjectDeleteMethod.RecycleBin:
                        MoveToRecycleBin(fullPath);
                        return true;
                    default:
                        error = "Unsupported delete method.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                if (attempt == attempts) return false;
                System.Threading.Thread.Sleep(100);
            }
        }

        return false;
    }

    private static void MoveToRecycleBin(string fullPath)
    {
#if NET472
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            throw new PlatformNotSupportedException("RecycleBin deletion is supported only on Windows.");
#else
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("RecycleBin deletion is supported only on Windows.");
#endif

        var op = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = $"{fullPath}\0\0",
            pTo = null,
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        var result = SHFileOperation(ref op);
        if (result != 0)
            throw new InvalidOperationException($"RecycleBin delete failed for '{fullPath}' (result: 0x{result:X8}).");

        if (op.fAnyOperationsAborted)
            throw new InvalidOperationException($"RecycleBin delete aborted for '{fullPath}'.");
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;

        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private static IEnumerable<ProjectItem> CollectItems(
        string projectRoot,
        CleanupPatterns patterns,
        string[] excludePatterns,
        string[] excludeDirectories,
        bool recurse,
        int maxDepth)
    {
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var excludePatternMatchers = excludePatterns
            .Select(p => new WildcardPattern(p, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

        var folderMatchers = patterns.Folders
            .Select(p => new WildcardPattern(p, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

        var fileMatchers = patterns.Files
            .Select(p => new WildcardPattern(p, WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant))
            .ToArray();

        var baseDir = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((baseDir, 0));

        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();

            IEnumerable<string> subDirs;
            IEnumerable<string> files;

            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                if (excludePatternMatchers.Any(m => m.IsMatch(name))) continue;

                var rel = ComputeRelativePath(baseDir, f);
                if (IsExcludedByParts(rel, excludeDirectories, patterns.ExcludeFolders)) continue;

                var pattern = FirstMatch(fileMatchers, name, patterns.Files);
                if (pattern is null) continue;

                if (!processed.Add(f)) continue;

                long size = 0;
                try { size = new FileInfo(f).Length; } catch { }

                yield return new ProjectItem
                {
                    FullPath = f,
                    RelativePath = rel,
                    Type = ProjectCleanupItemType.File,
                    Pattern = pattern,
                    Size = size
                };
            }

            foreach (var sd in subDirs)
            {
                var name = Path.GetFileName(sd);
                var rel = ComputeRelativePath(baseDir, sd);

                if (IsExcludedByParts(rel, excludeDirectories, patterns.ExcludeFolders))
                    continue;

                var pattern = FirstMatch(folderMatchers, name, patterns.Folders);
                if (pattern is not null)
                {
                    if (processed.Add(sd))
                    {
                        yield return new ProjectItem
                        {
                            FullPath = sd,
                            RelativePath = rel,
                            Type = ProjectCleanupItemType.Folder,
                            Pattern = pattern,
                            Size = 0
                        };
                    }
                }

                var nextDepth = depth + 1;
                if (!recurse) continue;
                if (maxDepth > 0 && nextDepth > maxDepth) continue;

                if (excludeDirectories.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
                if (patterns.ExcludeFolders.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;

                stack.Push((sd, nextDepth));
            }
        }
    }

    private static string? FirstMatch(WildcardPattern[] matchers, string value, string[] patterns)
    {
        for (var i = 0; i < matchers.Length; i++)
        {
            if (matchers[i].IsMatch(value))
                return patterns[i];
        }
        return null;
    }

    private static bool IsExcludedByParts(string relativePath, string[] excludeDirs, string[] specialExcludeFolders)
    {
        var parts = relativePath
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var ex in excludeDirs)
        {
            if (parts.Any(p => string.Equals(p, ex, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        foreach (var ex in specialExcludeFolders)
        {
            if (parts.Any(p => string.Equals(p, ex, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static void CreateItemBackups(List<ProjectItem> items, string backupDir)
    {
        foreach (var item in items)
        {
            try
            {
                var backupPath = Path.Combine(backupDir, item.RelativePath);
                var parent = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                if (item.Type == ProjectCleanupItemType.File)
                {
                    File.Copy(item.FullPath, backupPath, overwrite: true);
                }
                else
                {
                    CopyDirectory(item.FullPath, backupPath);
                }
            }
            catch
            {
                // best-effort
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private static int GetDepth(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return 0;
        var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length;
    }

    private sealed class CleanupPatterns
    {
        public string[] Folders { get; set; } = Array.Empty<string>();
        public string[] Files { get; set; } = Array.Empty<string>();
        public string[] ExcludeFolders { get; set; } = Array.Empty<string>();

        public static CleanupPatterns FromCustom(string[] includePatterns)
        {
            var folderPatterns = new List<string>();
            var filePatterns = new List<string>();

            var classifyAsFile = new WildcardPattern("*.*", WildcardOptions.IgnoreCase | WildcardOptions.CultureInvariant);
            foreach (var p in includePatterns ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (classifyAsFile.IsMatch(p)) filePatterns.Add(p);
                else folderPatterns.Add(p);
            }

            return new CleanupPatterns { Folders = folderPatterns.ToArray(), Files = filePatterns.ToArray(), ExcludeFolders = Array.Empty<string>() };
        }

        public static CleanupPatterns FromProjectType(ProjectCleanupType type)
        {
            return type switch
            {
                ProjectCleanupType.Build => new CleanupPatterns
                {
                    Folders = new[] { "bin", "obj", "packages", ".vs", ".vscode", "TestResults", "BenchmarkDotNet.Artifacts", "coverage", "x64", "x86", "Debug", "Release" },
                    Files = new[] { "*.pdb", "*.dll", "*.exe", "*.cache", "*.tlog", "*.lastbuildstate", "*.unsuccessfulbuild" },
                    ExcludeFolders = new[] { "Docs", "Examples", "Ignore" }
                },
                ProjectCleanupType.Logs => new CleanupPatterns
                {
                    Folders = new[] { "logs", "log" },
                    Files = new[] { "*.log", "*.tmp", "*.temp", "*.trace", "*.etl" },
                    ExcludeFolders = new[] { "Ignore" }
                },
                ProjectCleanupType.Html => new CleanupPatterns
                {
                    Folders = Array.Empty<string>(),
                    Files = new[] { "*.html", "*.htm" },
                    ExcludeFolders = new[] { "Assets", "Docs", "Examples", "Documentation", "Help", "Ignore" }
                },
                ProjectCleanupType.Temp => new CleanupPatterns
                {
                    Folders = new[] { "temp*", "tmp*", "cache*", ".temp", ".tmp" },
                    Files = new[] { "*.tmp", "*.temp", "*.cache", "~*", "thumbs.db", "desktop.ini" },
                    ExcludeFolders = new[] { "Ignore" }
                },
                ProjectCleanupType.All => new CleanupPatterns
                {
                    Folders = new[] { "bin", "obj", "packages", ".vs", ".vscode", "TestResults", "BenchmarkDotNet.Artifacts", "coverage", "x64", "x86", "Debug", "Release", "logs", "log", "temp*", "tmp*", "cache*", ".temp", ".tmp" },
                    Files = new[] { "*.pdb", "*.dll", "*.exe", "*.cache", "*.tlog", "*.lastbuildstate", "*.unsuccessfulbuild", "*.log", "*.tmp", "*.temp", "*.trace", "*.etl", "*.html", "*.htm", "~*", "thumbs.db", "desktop.ini" },
                    ExcludeFolders = new[] { "Assets", "Docs", "Examples", "Documentation", "Help", "Ignore" }
                },
                _ => new CleanupPatterns()
            };
        }
    }

    private sealed class ProjectItem
    {
        public string FullPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public ProjectCleanupItemType Type { get; set; }
        public string Pattern { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
