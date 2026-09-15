namespace PowerForge;

public sealed partial class AppStoreConnectReleasePreparationService
{
    private static AppStoreConnectReleaseScreenshotMapping[] ResolveScreenshotMappings(AppStoreConnectReleasePreparationRequest request)
    {
        var mappings = new List<AppStoreConnectReleaseScreenshotMapping>();
        if (request.ScreenshotSpec is not null)
            mappings.Add(new AppStoreConnectReleaseScreenshotMapping { Spec = request.ScreenshotSpec, BaseDirectory = request.BaseDirectory });
        mappings.AddRange(request.ScreenshotMappings ?? Array.Empty<AppStoreConnectReleaseScreenshotMapping>());
        if (mappings.Any(static mapping => mapping is null || mapping.Spec is null))
            throw new ArgumentException("Screenshot mappings must contain a spec.", nameof(request));
        ValidateScreenshotLocales(mappings.Select(static mapping => mapping.Spec));
        return mappings.ToArray();
    }

    internal static void ValidateScreenshotLocales(IEnumerable<AppStoreConnectScreenshotSyncSpec> specs)
    {
        var locales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs)
        {
            if (spec is null || string.IsNullOrWhiteSpace(spec.Locale))
                throw new InvalidOperationException("Screenshot locale is required.");
            if (!locales.Add(spec.Locale.Trim()))
                throw new InvalidOperationException($"Multiple screenshot sync configs target locale '{spec.Locale}'.");
        }
    }

    // Own every validated file snapshot before the first remote mutation, including on partial construction failure.
    private sealed class ScreenshotPreparationBatch : IDisposable
    {
        private readonly AppStoreConnectScreenshotSyncService _service;
        internal List<(AppStoreConnectScreenshotSyncRequest Request, AppStoreConnectScreenshotSyncService.ScreenshotSnapshot Snapshot)> Items { get; } = new();

        internal ScreenshotPreparationBatch(AppStoreConnectScreenshotSyncRequest[] requests, AppStoreConnectScreenshotSyncService service)
        {
            _service = service;
            try
            {
                foreach (var request in requests)
                    Items.Add((request, service.CreateSnapshot(request)));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal async Task AuthorizeInventoryAsync(string versionId, string expectedHash, CancellationToken cancellationToken)
        {
            var inventories = new List<AppStoreConnectReleaseScreenshotSetReadiness>();
            foreach (var item in Items)
            {
                item.Request.Spec.VersionId = versionId;
                var inventory = await _service.ReadRemoteInventoryAsync(item.Request.Spec, cancellationToken).ConfigureAwait(false);
                inventories.AddRange(inventory);
                item.Request.ExpectedRemoteInventorySha256 = AppStoreConnectScreenshotInventory.ComputeSha256(inventory);
            }
            var actualHash = AppStoreConnectScreenshotInventory.ComputeSha256(inventories);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("App Store Connect screenshots changed after Apple plan approval. Review a new exact screenshot replacement plan before any remote release mutation.");
        }

        public void Dispose()
        {
            foreach (var item in Items)
                item.Snapshot.Dispose();
            Items.Clear();
        }
    }
}
