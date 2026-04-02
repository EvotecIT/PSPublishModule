namespace PowerForge;

internal static class ModulePathTokenFormatter
{
    internal static string FormatVersionWithPreRelease(string moduleVersion, string? preRelease = null)
        => string.IsNullOrWhiteSpace(preRelease) ? (moduleVersion ?? string.Empty) : (moduleVersion ?? string.Empty) + "-" + preRelease;

    internal static string ReplacePathTokens(string? replacementPath, string moduleName, string moduleVersion, string? preRelease = null)
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
