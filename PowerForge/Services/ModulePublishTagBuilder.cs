namespace PowerForge;

/// <summary>
/// Builds GitHub release tags for module publish operations.
/// </summary>
public sealed class ModulePublishTagBuilder
{
    /// <summary>
    /// Builds the publish tag for the provided module publish configuration.
    /// </summary>
    /// <param name="publish">Publish configuration.</param>
    /// <param name="moduleName">Module name.</param>
    /// <param name="resolvedVersion">Resolved stable version.</param>
    /// <param name="preRelease">Optional prerelease label.</param>
    /// <returns>Resolved tag name.</returns>
    public string BuildTag(PublishConfiguration publish, string moduleName, string resolvedVersion, string? preRelease)
    {
        FrameworkCompatibility.NotNull(publish, nameof(publish));

        var versionWithPreRelease = string.IsNullOrWhiteSpace(preRelease)
            ? resolvedVersion
            : $"{resolvedVersion}-{preRelease}";

        if (string.IsNullOrWhiteSpace(publish.OverwriteTagName))
            return $"v{versionWithPreRelease}";

        return publish.OverwriteTagName!
            .Replace("<ModuleName>", moduleName)
            .Replace("{ModuleName}", moduleName)
            .Replace("<ModuleVersion>", resolvedVersion)
            .Replace("{ModuleVersion}", resolvedVersion)
            .Replace("<ModuleVersionWithPreRelease>", versionWithPreRelease)
            .Replace("{ModuleVersionWithPreRelease}", versionWithPreRelease)
            .Replace("<TagModuleVersionWithPreRelease>", $"v{versionWithPreRelease}")
            .Replace("{TagModuleVersionWithPreRelease}", $"v{versionWithPreRelease}");
    }
}
