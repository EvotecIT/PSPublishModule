namespace PowerForge;

/// <summary>
/// Status value for a single project cleanup result item.
/// </summary>
public enum ProjectCleanupStatus
{
    /// <summary>The item would be removed (WhatIf/ShouldProcess declined).</summary>
    WhatIf,
    /// <summary>The item was removed successfully.</summary>
    Removed,
    /// <summary>The item removal failed.</summary>
    Failed,
    /// <summary>An unexpected error occurred while attempting to remove the item.</summary>
    Error
}

