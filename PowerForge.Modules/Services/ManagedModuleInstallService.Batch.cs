namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    // Keep the official OData filter bounded even with long module names.
    private const int MaximumGalleryBatchSize = 12;

    internal async Task InstallBatchAsync(
        IReadOnlyList<ManagedModuleInstallRequest> requests,
        Action<ManagedModuleInstallResult> report,
        CancellationToken cancellationToken,
        Func<ManagedModuleInstallRequest, bool>? approve = null)
    {
        for (var offset = 0; offset < requests.Count; offset += MaximumGalleryBatchSize)
        {
            var group = requests.Skip(offset).Take(MaximumGalleryBatchSize).ToArray();
            var batch = CreateOfficialGalleryBatch(group, cancellationToken);
            foreach (var request in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (approve is not null && !approve(request))
                    continue;

                using var context = new ManagedModuleInstallContext();
                var result = await InstallAsync(request, context, cancellationToken, batch).ConfigureAwait(false);
                report(result);
            }
        }
    }

    private Lazy<Task<IReadOnlyDictionary<string, ManagedModuleVersionInfo>?>>? CreateOfficialGalleryBatch(
        ManagedModuleInstallRequest[] group,
        CancellationToken cancellationToken)
    {
        var first = group[0];
        if (group.Length < 2 || first.Repository is null ||
            !ManagedModuleRepositoryClient.SupportsOfficialGalleryBatch(first.Repository) ||
            group.Any(request => !CanResolveInGalleryBatch(request, first)))
            return null;

        var names = group.Select(static request => request.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length != group.Length)
            return null;

        // Start under the first normal install's request scope. A batch lookup
        // is charged once in the per-install telemetry, and only on demand.
        return new Lazy<Task<IReadOnlyDictionary<string, ManagedModuleVersionInfo>?>>(() =>
            _repositoryClient.TryGetOfficialGalleryLatestBatchAsync(
                first.Repository, names, first.IncludePrerelease, cancellationToken));
    }

    private static bool CanResolveInGalleryBatch(ManagedModuleInstallRequest request, ManagedModuleInstallRequest first)
    {
        if (!ReferenceEquals(request.Repository, first.Repository) || request.IncludePrerelease != first.IncludePrerelease ||
            request.Credential is not null || request.TrustPolicy is not null || request.SaveAsNupkg ||
            !string.IsNullOrWhiteSpace(request.ExpectedPackageSha256) ||
            !string.IsNullOrWhiteSpace(request.Version) || !string.IsNullOrWhiteSpace(request.MinimumVersion) ||
            !string.IsNullOrWhiteSpace(request.MaximumVersion) || !string.IsNullOrWhiteSpace(request.VersionPolicy))
            return false;

        var name = request.Name?.Trim();
        return name is { Length: > 0 and <= 100 } && name != "." && !name.Contains("..", StringComparison.Ordinal) &&
               name.All(static ch => ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-');
    }

    private static async Task<ManagedModuleVersionInfo?> GetBatchVersionAsync(
        Lazy<Task<IReadOnlyDictionary<string, ManagedModuleVersionInfo>?>> batch,
        string name)
    {
        var versions = await batch.Value.ConfigureAwait(false);
        return versions is not null && versions.TryGetValue(name.Trim(), out var version) ? version : null;
    }
}
