namespace PowerForge;

internal sealed partial class CatalogUpdateService
{
    // Progress is reusable only after fresh signature/identity/hash qualification of the selected release.
    private static void RequireSameRelease(CatalogUpdateReceipt previous, CatalogUpdateReceipt prepared)
    {
        bool Same(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
        if (!Same(previous.PackageIdentifier, prepared.PackageIdentifier) ||
            !Same(previous.PackageVersion, prepared.PackageVersion) ||
            !Same(previous.DeliveryReleaseId, prepared.DeliveryReleaseId) ||
            previous.Files.Count != prepared.Files.Count || previous.Artifacts.Length != prepared.Artifacts.Length ||
            previous.Files.Any(file => !prepared.Files.TryGetValue(file.Key, out var hash) || !Same(file.Value, hash)))
            throw new InvalidOperationException("Restored catalog receipt differs from the selected signed release. Reconcile before continuing.");
        for (var i = 0; i < prepared.Artifacts.Length; i++)
        {
            var left = previous.Artifacts[i]; var right = prepared.Artifacts[i];
            if (!Same(left.FileName, right.FileName) || !Same(left.Architecture, right.Architecture) ||
                !Same(left.Sha256, right.Sha256) || left.Length != right.Length ||
                !Same(left.WingetUrl, right.WingetUrl) || !Same(left.StoreUrl, right.StoreUrl))
                throw new InvalidOperationException("Restored installer metadata differs from the selected signed release. Reconcile before continuing.");
        }
    }
}
