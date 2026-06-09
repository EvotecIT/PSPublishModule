namespace PowerForge;

/// <summary>
/// Replaces common module/version path tokens used across packaging and publish workflows.
/// </summary>
public static class ModulePathTokenFormatter
{
    /// <summary>
    /// Formats a module version and optional prerelease suffix.
    /// </summary>
    public static string FormatVersionWithPreRelease(string moduleVersion, string? preRelease = null)
        => string.IsNullOrWhiteSpace(preRelease) ? (moduleVersion ?? string.Empty) : (moduleVersion ?? string.Empty) + "-" + preRelease;

    /// <summary>
    /// Replaces common PSPublishModule path tokens (for example <c>&lt;ModuleName&gt;</c> and <c>&lt;ModuleVersion&gt;</c>).
    /// </summary>
    public static string ReplacePathTokens(string? replacementPath, string moduleName, string moduleVersion, string? preRelease = null)
    {
        if (replacementPath is null)
            return string.Empty;

        var tagName = "v" + moduleVersion;
        var moduleVersionWithPreRelease = FormatVersionWithPreRelease(moduleVersion, preRelease);
        var tagModuleVersionWithPreRelease = "v" + moduleVersionWithPreRelease;

        var path = replacementPath;
        path = path.Replace("<TagName>", tagName).Replace("{TagName}", tagName);
        path = path.Replace("<ModuleVersion>", moduleVersion).Replace("{ModuleVersion}", moduleVersion);
        path = path.Replace("<ModuleVersionWithPreRelease>", moduleVersionWithPreRelease).Replace("{ModuleVersionWithPreRelease}", moduleVersionWithPreRelease);
        path = path.Replace("<TagModuleVersionWithPreRelease>", tagModuleVersionWithPreRelease).Replace("{TagModuleVersionWithPreRelease}", tagModuleVersionWithPreRelease);
        path = path.Replace("<ModuleName>", moduleName).Replace("{ModuleName}", moduleName);

        return path;
    }
}
