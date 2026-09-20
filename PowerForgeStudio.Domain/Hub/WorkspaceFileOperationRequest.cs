namespace PowerForgeStudio.Domain.Hub;

/// <summary>Concrete source and destination paths displayed before a file operation.</summary>
public sealed record WorkspaceFileOperationRequest(
    WorkspaceFileOperation Operation,
    string WorkingCopyRoot,
    string? SourcePath,
    string DestinationPath);
