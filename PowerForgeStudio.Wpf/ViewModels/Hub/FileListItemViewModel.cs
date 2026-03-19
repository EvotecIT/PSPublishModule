using System.Windows.Media;
using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class FileListItemViewModel : ViewModelBase
{
    public FileListItemViewModel(FileSystemEntry entry)
    {
        Name = entry.Name;
        FullPath = entry.FullPath;
        IsDirectory = entry.IsDirectory;
        SizeBytes = entry.SizeBytes;
        LastModifiedUtc = entry.LastModifiedUtc;
        Icon = FileIconCache.GetIcon(entry.FullPath, entry.IsDirectory);
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long SizeBytes { get; }
    public DateTimeOffset LastModifiedUtc { get; }
    public ImageSource? Icon { get; }

    public string SizeDisplay
    {
        get
        {
            if (IsDirectory) return string.Empty;
            return SizeBytes switch
            {
                < 1024 => $"{SizeBytes} B",
                < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
                < 1024 * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
                _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F1} GB"
            };
        }
    }

    public string LastModifiedDisplay => Domain.Hub.RelativeTimeFormatter.FormatWithAgo(LastModifiedUtc);
}
