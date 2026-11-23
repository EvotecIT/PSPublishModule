namespace PowerForge;

/// <summary>
/// Profile store scope used by private gallery profile commands.
/// </summary>
public enum ModuleRepositoryProfileScope
{
    /// <summary>Use the current user's profile store.</summary>
    User,
    /// <summary>Use the machine-wide profile store shared by all users on the workstation.</summary>
    Machine,
    /// <summary>Read from user and machine-wide stores, preferring the user store when both define the same profile.</summary>
    All
}
