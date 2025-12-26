using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Removes specific files and folders from a project directory with safety features.
/// </summary>
[Cmdlet(VerbsCommon.Remove, "ProjectFiles", SupportsShouldProcess = true, DefaultParameterSetName = ParameterSetProjectType)]
public sealed class RemoveProjectFilesCommand : PSCmdlet
{
    private const string ParameterSetProjectType = "ProjectType";
    private const string ParameterSetCustom = "Custom";

    /// <summary>Path to the project directory to clean.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Type of project cleanup to perform.</summary>
    [Parameter(ParameterSetName = ParameterSetProjectType)]
    public ProjectCleanupType ProjectType { get; set; } = ProjectCleanupType.Build;

    /// <summary>File/folder patterns to include for deletion when using the Custom parameter set.</summary>
    [Parameter(ParameterSetName = ParameterSetCustom, Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string[] IncludePatterns { get; set; } = Array.Empty<string>();

    /// <summary>Patterns to exclude from deletion.</summary>
    [Parameter]
    public string[]? ExcludePatterns { get; set; }

    /// <summary>Directory names to completely exclude from processing.</summary>
    [Parameter]
    public string[] ExcludeDirectories { get; set; } = { ".git", ".svn", ".hg", "node_modules" };

    /// <summary>Method to use for deletion.</summary>
    [Parameter]
    public ProjectDeleteMethod DeleteMethod { get; set; } = ProjectDeleteMethod.RemoveItem;

    /// <summary>Create backup copies of items before deletion.</summary>
    [Parameter]
    public SwitchParameter CreateBackups { get; set; }

    /// <summary>Directory where backups should be stored (optional).</summary>
    [Parameter]
    public string? BackupDirectory { get; set; }

    /// <summary>Number of retry attempts for each deletion.</summary>
    [Parameter]
    public int Retries { get; set; } = 3;

    /// <summary>Process subdirectories recursively. Defaults to true unless explicitly specified.</summary>
    [Parameter]
    public SwitchParameter Recurse { get; set; }

    /// <summary>Maximum recursion depth. Default is unlimited (-1).</summary>
    [Parameter]
    public int MaxDepth { get; set; } = -1;

    /// <summary>Display progress information during cleanup.</summary>
    [Parameter]
    public SwitchParameter ShowProgress { get; set; }

    /// <summary>Return detailed results.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Suppress console output and use verbose/warning streams instead.</summary>
    [Parameter]
    public SwitchParameter Internal { get; set; }

    /// <summary>Executes the cleanup.</summary>
    protected override void ProcessRecord()
    {
        var root = SessionState.Path.GetUnresolvedProviderPathFromPSPath(ProjectPath);
        if (!Directory.Exists(root))
        {
            throw new PSArgumentException($"Project path '{ProjectPath}' not found or is not a directory");
        }

        var recurse = MyInvocation.BoundParameters.ContainsKey(nameof(Recurse)) ? Recurse.IsPresent : true;
        var patterns = ParameterSetName == ParameterSetCustom
            ? CleanupPatterns.FromCustom(IncludePatterns)
            : CleanupPatterns.FromProjectType(ProjectType);

        if (Internal.IsPresent)
        {
            WriteVerbose($"Processing project cleanup for: {root}");
            WriteVerbose($"Folder patterns: {string.Join(", ", patterns.Folders)}");
            WriteVerbose($"File patterns: {string.Join(", ", patterns.Files)}");
        }
        else
        {
            WriteHostLine($"Processing project cleanup for: {root}", ConsoleColor.Cyan);
            WriteHostLine(ParameterSetName == ParameterSetCustom
                ? $"Project type: Custom with patterns: {string.Join(", ", IncludePatterns ?? Array.Empty<string>())}"
                : $"Project type: {ProjectType}", ConsoleColor.White);
        }

        if (CreateBackups.IsPresent)
        {
            if (string.IsNullOrWhiteSpace(BackupDirectory))
            {
                BackupDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"PSPublishModule_Backup_{DateTime.Now:yyyyMMdd_HHmmss}");
            }

            if (!Directory.Exists(BackupDirectory))
            {
                Directory.CreateDirectory(BackupDirectory);
                if (Internal.IsPresent) WriteVerbose($"Created backup directory: {BackupDirectory}");
                else WriteHostLine($"Created backup directory: {BackupDirectory}", ConsoleColor.Green);
            }
        }

        if (Internal.IsPresent) WriteVerbose("Scanning for files to remove...");
        else WriteHostLine("Scanning for files to remove...", ConsoleColor.Cyan);

        var excludePatterns = (ExcludePatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var excludeDirs = (ExcludeDirectories ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();

        var items = CollectItems(root, patterns, excludePatterns, excludeDirs, recurse, MaxDepth)
            .OrderByDescending(i => GetDepth(i.RelativePath))
            .ToList();
        if (items.Count == 0)
        {
            if (Internal.IsPresent) WriteVerbose("No files or folders found matching the specified criteria.");
            else WriteHostLine("No files or folders found matching the specified criteria.", ConsoleColor.Yellow);
            return;
        }

        var totalSize = items.Where(i => i.Type == "File").Sum(i => i.Size);
        var totalSizeMb = Math.Round(totalSize / (1024d * 1024d), 2);

        if (Internal.IsPresent)
        {
            WriteVerbose($"Found {items.Count} items to remove");
            WriteVerbose($"Files: {items.Count(i => i.Type == "File")}");
            WriteVerbose($"Folders: {items.Count(i => i.Type == "Folder")}");
            WriteVerbose($"Total size: {totalSizeMb} MB");
        }
        else
        {
            WriteHostLine($"Found {items.Count} items to remove:", ConsoleColor.Green);
            WriteHostLine($"  Files: {items.Count(i => i.Type == "File")}", ConsoleColor.White);
            WriteHostLine($"  Folders: {items.Count(i => i.Type == "Folder")}", ConsoleColor.White);
            WriteHostLine($"  Total size: {totalSizeMb} MB", ConsoleColor.White);
        }

        if (ShowProgress.IsPresent && !Internal.IsPresent)
        {
            WriteHostLine(string.Empty, ConsoleColor.White);
            WriteHostLine("Items to be removed:", ConsoleColor.Cyan);
            foreach (var item in items.Take(10))
            {
                var sizeInfo = item.Type == "File" ? $" ({Math.Round(item.Size / 1024d, 1)} KB)" : string.Empty;
                WriteHostLine($"  [{item.Type}] {item.RelativePath}{sizeInfo}", ConsoleColor.Gray);
            }
            if (items.Count > 10)
            {
                WriteHostLine($"  ... and {items.Count - 10} more items", ConsoleColor.Gray);
            }
        }

        if (CreateBackups.IsPresent && !string.IsNullOrWhiteSpace(BackupDirectory))
        {
            var (created, backupErrors) = CreateItemBackups(items, BackupDirectory!);
            if (Internal.IsPresent)
            {
                WriteVerbose($"Created {created} backups with {backupErrors} errors");
            }
            else
            {
                WriteHostLine($"Created {created} backups", ConsoleColor.Green);
                if (backupErrors > 0)
                {
                    WriteHostLine($"Backup errors: {backupErrors}", ConsoleColor.Yellow);
                }
            }
        }

        var isWhatIf = MyInvocation.BoundParameters.ContainsKey("WhatIf") && ((SwitchParameter)MyInvocation.BoundParameters["WhatIf"]).IsPresent;
        var results = new List<RemovalResult>(items.Count);

        var totalItems = items.Count;
        for (var idx = 0; idx < items.Count; idx++)
        {
            var item = items[idx];
            var r = new RemovalResult
            {
                RelativePath = item.RelativePath,
                FullPath = item.FullPath,
                Type = item.Type,
                Pattern = item.Pattern,
                Status = "Unknown",
                Size = item.Size,
                Error = null
            };

            if (isWhatIf)
            {
                r.Status = "WhatIf";
                if (ShowProgress.IsPresent && !Internal.IsPresent)
                {
                    WriteHostLine($"  [WOULD REMOVE] {item.RelativePath}", ConsoleColor.Yellow);
                }
                else if (Internal.IsPresent)
                {
                    WriteVerbose($"Would remove: {item.RelativePath}");
                }

                results.Add(r);
                continue;
            }

            try
            {
                var action = $"Remove ({DeleteMethod})";
                if (!ShouldProcess(item.FullPath, action))
                {
                    r.Status = "WhatIf";
                    results.Add(r);
                    continue;
                }

                var ok = TryDeleteWithRetries(item.FullPath, item.Type == "Folder", DeleteMethod, Retries, out var err);
                if (ok)
                {
                    r.Status = "Removed";
                    if (ShowProgress.IsPresent && !Internal.IsPresent)
                    {
                        WriteHostLine($"  [{idx + 1}/{totalItems}] [REMOVED] {item.RelativePath}", ConsoleColor.Red);
                    }
                    else if (Internal.IsPresent)
                    {
                        WriteVerbose($"Removed: {item.RelativePath}");
                    }
                }
                else
                {
                    r.Status = "Failed";
                    r.Error = err ?? "Delete returned false";
                    if (ShowProgress.IsPresent && !Internal.IsPresent)
                    {
                        WriteHostLine($"  [{idx + 1}/{totalItems}] [FAILED] {item.RelativePath}", ConsoleColor.Red);
                    }
                    else if (Internal.IsPresent)
                    {
                        WriteWarning($"Failed to remove: {item.RelativePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                r.Status = "Error";
                r.Error = ex.Message;
                if (ShowProgress.IsPresent && !Internal.IsPresent)
                {
                    WriteHostLine($"  [{idx + 1}/{totalItems}] [ERROR] {item.RelativePath}: {ex.Message}", ConsoleColor.Red);
                }
                else
                {
                    WriteWarning($"Failed to remove '{item.RelativePath}': {ex.Message}");
                }
            }

            results.Add(r);
        }

        var removed = results.Count(x => x.Status == "Removed");
        var errors = results.Count(x => x.Status == "Error");
        var freed = results.Where(x => x.Status == "Removed").Sum(x => x.Size);
        var freedMb = Math.Round(freed / (1024d * 1024d), 2);

        var displayProjectType = ParameterSetName == ParameterSetCustom ? "Custom" : ProjectType.ToString();

        if (Internal.IsPresent)
        {
            WriteVerbose($"Cleanup Summary: Project path: {root}");
            WriteVerbose($"Cleanup type: {displayProjectType}");
            WriteVerbose($"Total items processed: {items.Count}");

            if (isWhatIf)
            {
                WriteVerbose($"Would remove: {items.Count} items");
                WriteVerbose($"Would free: {totalSizeMb} MB");
            }
            else
            {
                WriteVerbose($"Successfully removed: {removed}");
                WriteVerbose($"Errors: {errors}");
                WriteVerbose($"Space freed: {freedMb} MB");
                if (CreateBackups.IsPresent) WriteVerbose($"Backups created in: {BackupDirectory}");
            }
        }
        else
        {
            WriteHostLine(string.Empty, ConsoleColor.White);
            WriteHostLine("Cleanup Summary:", ConsoleColor.Cyan);
            WriteHostLine($"  Project path: {root}", ConsoleColor.White);
            WriteHostLine($"  Cleanup type: {displayProjectType}", ConsoleColor.White);
            WriteHostLine($"  Total items processed: {items.Count}", ConsoleColor.White);

            if (isWhatIf)
            {
                WriteHostLine($"  Would remove: {items.Count} items", ConsoleColor.Yellow);
                WriteHostLine($"  Would free: {totalSizeMb} MB", ConsoleColor.Yellow);
                WriteHostLine("Run without -WhatIf to actually remove these items.", ConsoleColor.Cyan);
            }
            else
            {
                WriteHostLine($"  Successfully removed: {removed}", ConsoleColor.Green);
                WriteHostLine($"  Errors: {errors}", ConsoleColor.Red);
                WriteHostLine($"  Space freed: {freedMb} MB", ConsoleColor.Green);
                if (CreateBackups.IsPresent) WriteHostLine($"  Backups created in: {BackupDirectory}", ConsoleColor.Blue);
            }
        }

        if (PassThru.IsPresent)
        {
            var summary = new RemoveProjectFilesSummary
            {
                ProjectPath = root,
                ProjectType = displayProjectType,
                TotalItems = items.Count,
                Removed = removed,
                Errors = errors,
                SpaceFreed = freed,
                SpaceFreedMB = freedMb,
                BackupDirectory = CreateBackups.IsPresent ? BackupDirectory : null,
                DeleteMethod = DeleteMethod.ToString()
            };
            WriteObject(new RemoveProjectFilesOutput { Summary = summary, Results = results.ToArray() });
        }
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
                        if (!IsWindows())
                            throw new PlatformNotSupportedException("RecycleBin deletion is supported only on Windows.");
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
        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is null) throw new InvalidOperationException("Shell.Application COM object is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;

        var folderPath = System.IO.Path.GetDirectoryName(fullPath);
        var leaf = System.IO.Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(leaf))
            throw new InvalidOperationException($"Invalid path: {fullPath}");

        dynamic folder = shell.NameSpace(folderPath);
        if (folder is null) throw new InvalidOperationException($"Could not open folder: {folderPath}");

        dynamic item = folder.ParseName(leaf);
        if (item is null) throw new InvalidOperationException($"Item '{leaf}' not found in folder: {folderPath}");

        item.InvokeVerb("delete");
    }

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

        var baseDir = projectRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

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
                var name = System.IO.Path.GetFileName(f);
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
                    Type = "File",
                    Pattern = pattern,
                    Size = size
                };
            }

            foreach (var sd in subDirs)
            {
                var name = System.IO.Path.GetFileName(sd);
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
                            Type = "Folder",
                            Pattern = pattern,
                            Size = 0
                        };
                    }
                }

                var nextDepth = depth + 1;
                if (!recurse) continue;
                if (maxDepth > 0 && nextDepth > maxDepth) continue;

                // Skip traversing into excluded directories to match intent.
                if (excludeDirectories.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;
                if (patterns.ExcludeFolders.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) continue;

                stack.Push((sd, nextDepth));
            }
        }

        // Sort deepest-first for safe deletion.
        // (Write order is controlled by consumer.)
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
            .Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

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
            var baseUri = new Uri(AppendDirectorySeparatorChar(System.IO.Path.GetFullPath(baseDir)));
            var pathUri = new Uri(System.IO.Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', System.IO.Path.DirectorySeparatorChar);
        }
        catch
        {
            return System.IO.Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + System.IO.Path.DirectorySeparatorChar;

    private static (int Created, int Errors) CreateItemBackups(List<ProjectItem> items, string backupDir)
    {
        var created = 0;
        var errors = 0;

        foreach (var item in items)
        {
            try
            {
                var backupPath = System.IO.Path.Combine(backupDir, item.RelativePath);
                var parent = System.IO.Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                if (item.Type == "File")
                {
                    File.Copy(item.FullPath, backupPath, overwrite: true);
                }
                else
                {
                    CopyDirectory(item.FullPath, backupPath);
                }

                created++;
            }
            catch
            {
                errors++;
            }
        }

        return (created, errors);
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file);
            var dest = System.IO.Path.Combine(destDir, rel);
            var parent = System.IO.Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);
            File.Copy(file, dest, overwrite: true);
        }
    }

    private void WriteHostLine(string message, ConsoleColor color)
    {
        try
        {
            if (Host?.UI?.RawUI is not null)
            {
                Host.UI.Write(color, Host.UI.RawUI.BackgroundColor, message + Environment.NewLine);
            }
        }
        catch
        {
            WriteVerbose(message);
        }
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
                    ExcludeFolders = new[] { "Ignore" }
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
        public string Type { get; set; } = string.Empty;
        public string Pattern { get; set; } = string.Empty;
        public long Size { get; set; }
    }

    /// <summary>Single item removal result.</summary>
    public sealed class RemovalResult
    {
        /// <summary>Path relative to the project root.</summary>
        public string RelativePath { get; set; } = string.Empty;
        /// <summary>Absolute path.</summary>
        public string FullPath { get; set; } = string.Empty;
        /// <summary>Item type (File/Folder).</summary>
        public string Type { get; set; } = string.Empty;
        /// <summary>Matched pattern.</summary>
        public string Pattern { get; set; } = string.Empty;
        /// <summary>Status (Removed/Failed/Error/WhatIf).</summary>
        public string Status { get; set; } = string.Empty;
        /// <summary>File size in bytes (0 for folders).</summary>
        public long Size { get; set; }
        /// <summary>Error message when applicable.</summary>
        public string? Error { get; set; }
    }

    /// <summary>Summary returned by <c>Remove-ProjectFiles -PassThru</c>.</summary>
    public sealed class RemoveProjectFilesSummary
    {
        /// <summary>Project path.</summary>
        public string ProjectPath { get; set; } = string.Empty;
        /// <summary>Cleanup type.</summary>
        public string ProjectType { get; set; } = string.Empty;
        /// <summary>Total items processed.</summary>
        public int TotalItems { get; set; }
        /// <summary>Removed items count.</summary>
        public int Removed { get; set; }
        /// <summary>Error items count.</summary>
        public int Errors { get; set; }
        /// <summary>Space freed in bytes (Removed items only).</summary>
        public long SpaceFreed { get; set; }
        /// <summary>Space freed in MB (Removed items only).</summary>
        public double SpaceFreedMB { get; set; }
        /// <summary>Backup directory when enabled.</summary>
        public string? BackupDirectory { get; set; }
        /// <summary>Delete method.</summary>
        public string DeleteMethod { get; set; } = string.Empty;
    }

    /// <summary>Output object returned by <c>Remove-ProjectFiles -PassThru</c>.</summary>
    public sealed class RemoveProjectFilesOutput
    {
        /// <summary>Summary object.</summary>
        public RemoveProjectFilesSummary Summary { get; set; } = new();

        /// <summary>Per-item results.</summary>
        public RemovalResult[] Results { get; set; } = Array.Empty<RemovalResult>();
    }

    private static bool IsWindows()
    {
#if NET472
        return Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif
    }

    private static int GetDepth(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return 0;
        var parts = relativePath.Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length;
    }
}
