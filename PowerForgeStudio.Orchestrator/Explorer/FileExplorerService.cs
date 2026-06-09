using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Explorer;

public sealed class FileExplorerService
{
    private readonly FileExplorerOptions _options;

    public FileExplorerService(FileExplorerOptions? options = null)
    {
        _options = options ?? new FileExplorerOptions();
    }

    public Task<IReadOnlyList<FileSystemEntry>> ListDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ListDirectory(directoryPath), cancellationToken);
    }

    public FileSystemWatcher CreateWatcher(string directoryPath)
    {
        var watcher = new FileSystemWatcher(directoryPath)
        {
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        return watcher;
    }

    private IReadOnlyList<FileSystemEntry> ListDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        var entries = new List<FileSystemEntry>();

        // Directories first
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directoryPath))
            {
                var dirName = Path.GetFileName(dir);

                if (_options.ExcludedFolders.Contains(dirName))
                {
                    continue;
                }

                if (!_options.ShowHiddenFiles)
                {
                    try
                    {
                        var attrs = File.GetAttributes(dir);
                        if (attrs.HasFlag(FileAttributes.Hidden))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                try
                {
                    var info = new DirectoryInfo(dir);
                    entries.Add(new FileSystemEntry(
                        Name: dirName,
                        FullPath: dir,
                        IsDirectory: true,
                        SizeBytes: 0,
                        LastModifiedUtc: info.LastWriteTimeUtc));
                }
                catch
                {
                    // Skip inaccessible directories
                }
            }
        }
        catch
        {
            // Access denied to parent
        }

        // Then files
        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath))
            {
                var fileName = Path.GetFileName(file);

                if (!_options.ShowHiddenFiles)
                {
                    try
                    {
                        var attrs = File.GetAttributes(file);
                        if (attrs.HasFlag(FileAttributes.Hidden))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                try
                {
                    var info = new FileInfo(file);
                    entries.Add(new FileSystemEntry(
                        Name: fileName,
                        FullPath: file,
                        IsDirectory: false,
                        SizeBytes: info.Length,
                        LastModifiedUtc: info.LastWriteTimeUtc));
                }
                catch
                {
                    // Skip inaccessible files
                }
            }
        }
        catch
        {
            // Access denied
        }

        // Sort: directories first (alpha), then files (alpha)
        entries.Sort((a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
            {
                return a.IsDirectory ? -1 : 1;
            }

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return entries;
    }
}
