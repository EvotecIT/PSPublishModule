namespace PowerForge;

internal sealed class GitHubReleaseProgressAdapter : IGitHubReleaseProgressReporter
{
    private readonly IPowerForgeReleaseProgressReporterV2 _release;
    private readonly Dictionary<string, PowerForgeReleaseProgressItem> _items =
        new(StringComparer.OrdinalIgnoreCase);

    internal GitHubReleaseProgressAdapter(IPowerForgeReleaseProgressReporterV2 release)
        => _release = release ?? throw new ArgumentNullException(nameof(release));

    public void Report(GitHubReleaseAssetProgress progress)
    {
        if (progress is null || string.IsNullOrWhiteSpace(progress.FilePath))
            return;

        var item = GetOrCreate(progress);
        item.ProgressValue = Math.Max(0, progress.BytesTransferred);
        item.ProgressMaximum = Math.Max(0, progress.TotalBytes);

        switch (progress.State)
        {
            case GitHubReleaseAssetProgressState.Planned:
                return;
            case GitHubReleaseAssetProgressState.Replacing:
                _release.ItemUpdated(
                    item,
                    PowerForgeReleaseProgressItemState.Started,
                    BuildDetail(progress, "Replacing existing asset"));
                return;
            case GitHubReleaseAssetProgressState.Uploading:
                _release.ItemUpdated(
                    item,
                    PowerForgeReleaseProgressItemState.Started,
                    BuildTransferDetail(progress));
                return;
            case GitHubReleaseAssetProgressState.Uploaded:
                _release.ItemUpdated(
                    item,
                    PowerForgeReleaseProgressItemState.Completed,
                    BuildDetail(progress, "Uploaded"));
                return;
            case GitHubReleaseAssetProgressState.Skipped:
                _release.ItemUpdated(
                    item,
                    PowerForgeReleaseProgressItemState.Skipped,
                    progress.Detail ?? "Already exists");
                return;
            case GitHubReleaseAssetProgressState.Failed:
                _release.ItemUpdated(
                    item,
                    PowerForgeReleaseProgressItemState.Failed,
                    progress.Detail);
                return;
        }
    }

    private PowerForgeReleaseProgressItem GetOrCreate(GitHubReleaseAssetProgress progress)
    {
        if (_items.TryGetValue(progress.FilePath, out var item))
            return item;

        item = new PowerForgeReleaseProgressItem
        {
            Phase = PowerForgeReleaseProgressPhase.GitHub,
            Key = progress.FilePath,
            Title = progress.FileName,
            Kind = "GitHubAsset",
            Position = progress.Position,
            Total = progress.TotalAssets,
            ProgressMaximum = Math.Max(0, progress.TotalBytes)
        };
        _items[progress.FilePath] = item;
        _release.ItemsPlanned(PowerForgeReleaseProgressPhase.GitHub, new[] { item });
        return item;
    }

    private static string BuildTransferDetail(GitHubReleaseAssetProgress progress)
    {
        if (progress.TotalBytes <= 0)
            return "Uploading";

        return $"{DotNetRepositoryReleaseService.FormatBytes(progress.BytesTransferred)} / " +
               $"{DotNetRepositoryReleaseService.FormatBytes(progress.TotalBytes)}";
    }

    private static string BuildDetail(GitHubReleaseAssetProgress progress, string label)
        => progress.TotalBytes > 0
            ? $"{label} {DotNetRepositoryReleaseService.FormatBytes(progress.TotalBytes)}"
            : label;
}
