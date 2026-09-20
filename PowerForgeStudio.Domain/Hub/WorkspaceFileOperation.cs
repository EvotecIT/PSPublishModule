namespace PowerForgeStudio.Domain.Hub;

/// <summary>Explicit file-management actions inside one selected working copy.</summary>
public enum WorkspaceFileOperation
{
    CreateFile,
    CreateDirectory,
    Copy,
    Move,
    Rename
}
