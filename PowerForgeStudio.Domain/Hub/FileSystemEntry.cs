namespace PowerForgeStudio.Domain.Hub;

public sealed record FileSystemEntry(
    string Name,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc)
{
    public string Extension => IsDirectory ? string.Empty : System.IO.Path.GetExtension(Name);
}
