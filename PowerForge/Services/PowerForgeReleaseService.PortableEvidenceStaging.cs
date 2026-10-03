namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    // Detached evidence follows the runnable artifact's name and directory, even
    // when the release has separate metadata paths or filename templates.
    private static string? ResolveDetachedPortableEvidenceStagePath(
        PowerForgeReleaseAssetEntry entry,
        IReadOnlyList<PowerForgeReleaseAssetEntry> entries,
        PowerForgeReleaseStagingOptions options)
    {
        if (entry.Category != PowerForgeReleaseAssetCategory.Metadata ||
            !string.Equals(entry.Source, "DotNetPublish", StringComparison.OrdinalIgnoreCase))
            return null;

        string? suffix = new[]
        {
            PowerForgePortablePayloadInventory.DirectInventorySuffix,
            PowerForgePortablePayloadInventory.DirectSignatureSuffix
        }.FirstOrDefault(value => entry.Path.EndsWith(value, StringComparison.OrdinalIgnoreCase));
        if (suffix is null)
            return null;

        string parentPath = Path.GetFullPath(entry.Path.Substring(0, entry.Path.Length - suffix.Length));
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        PowerForgeReleaseAssetEntry parent = entries.SingleOrDefault(candidate =>
            candidate.Category != PowerForgeReleaseAssetCategory.Metadata &&
            !string.IsNullOrWhiteSpace(candidate.Path) &&
            string.Equals(Path.GetFullPath(candidate.Path), parentPath, comparison))
            ?? throw new InvalidOperationException($"Detached portable evidence '{entry.Path}' has no runnable release artifact.");

        return Path.Combine(
            ResolveStageDirectory(options, parent.Category),
            GetStageEntryName(parent, isDirectory: false, options) + suffix);
    }
}
