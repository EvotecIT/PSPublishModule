namespace PowerForge;

/// <summary>Existing App Store Connect team user and app visibility returned by Apple.</summary>
public sealed class AppStoreConnectUserInfo
{
    /// <summary>User resource id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>User's Apple Account username.</summary>
    public string? Username { get; set; }

    /// <summary>Assigned roles returned by Apple; empty when none were returned.</summary>
    public string[] Roles { get; set; } = Array.Empty<string>();

    /// <summary>Whether all apps are visible, or null when Apple did not return the value.</summary>
    public bool? AllAppsVisible { get; set; }
}
