namespace PowerForgeStudio.Domain.Hub;

/// <summary>Progress for the current file in an explicit workspace copy.</summary>
public sealed record FileTransferProgress(string SourcePath, long BytesCopied, long TotalBytes);
