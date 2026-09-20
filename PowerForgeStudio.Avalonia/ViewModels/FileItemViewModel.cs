using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

/// <summary>File-list display fields over the shared filesystem entry.</summary>
public sealed record FileItemViewModel(FileSystemEntry Entry)
{
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public string Extension => Entry.Extension;
    public string SizeDisplay => IsDirectory ? "" : Entry.SizeBytes >= 1024 * 1024
        ? $"{Entry.SizeBytes / (1024d * 1024):0.0} MiB"
        : Entry.SizeBytes >= 1024 ? $"{Entry.SizeBytes / 1024d:0.#} KiB" : $"{Entry.SizeBytes} B";
    public string IconKind => IsDirectory ? "folder" : Extension.ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" => "script", ".json" => "json", ".md" => "markdown",
        ".sln" or ".slnx" => "solution", _ => "file"
    };
}
