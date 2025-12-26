namespace PowerForge;

/// <summary>
/// Defines the deletion method used by <see cref="ProjectCleanupService"/>.
/// </summary>
public enum ProjectDeleteMethod
{
    /// <summary>Use standard file system delete operations.</summary>
    RemoveItem,
    /// <summary>
    /// Use <c>System.IO</c> delete operations (useful for some cloud-file edge cases).
    /// </summary>
    DotNetDelete,
    /// <summary>Move items to the Recycle Bin (Windows only).</summary>
    RecycleBin
}

