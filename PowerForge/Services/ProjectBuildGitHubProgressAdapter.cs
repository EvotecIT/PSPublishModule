namespace PowerForge;

/// <summary>
/// Maps GitHub asset transfer progress into the project-build detailed progress contract.
/// </summary>
internal sealed class ProjectBuildGitHubProgressAdapter : IGitHubReleaseProgressReporter
{
    private readonly IProjectBuildProgressReporterV2 _progress;
    private readonly IReadOnlyDictionary<string, AssetPlanItem> _plan;
    private readonly Dictionary<string, ProjectBuildProgressItem> _items =
        new(StringComparer.Ordinal);

    internal ProjectBuildGitHubProgressAdapter(
        IProjectBuildProgressReporterV2 progress,
        IReadOnlyList<string> assetPaths)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        if (assetPaths is null) throw new ArgumentNullException(nameof(assetPaths));

        var plan = new Dictionary<string, AssetPlanItem>(StringComparer.Ordinal);
        foreach (var path in assetPaths.Where(static path => !string.IsNullOrWhiteSpace(path)))
        {
            var identity = GetIdentity(path);
            if (plan.ContainsKey(identity))
                continue;

            plan[identity] = new AssetPlanItem(plan.Count + 1, identity);
        }

        var total = plan.Count;
        _plan = plan.ToDictionary(
            static pair => pair.Key,
            pair => new AssetPlanItem(pair.Value.Position, pair.Value.Identity, total),
            StringComparer.Ordinal);
    }

    public void Report(GitHubReleaseAssetProgress progress)
    {
        if (progress is null)
            return;

        var item = GetOrCreate(progress);
        _progress.ItemUpdated(
            item,
            progress.State switch
            {
                GitHubReleaseAssetProgressState.Replacing => ProjectBuildProgressItemState.Started,
                GitHubReleaseAssetProgressState.Uploading => ProjectBuildProgressItemState.Started,
                GitHubReleaseAssetProgressState.Uploaded => ProjectBuildProgressItemState.Completed,
                GitHubReleaseAssetProgressState.Skipped => ProjectBuildProgressItemState.Skipped,
                GitHubReleaseAssetProgressState.Failed => ProjectBuildProgressItemState.Failed,
                _ => ProjectBuildProgressItemState.Planned
            },
            BuildDetail(progress));
    }

    private ProjectBuildProgressItem GetOrCreate(GitHubReleaseAssetProgress progress)
    {
        var identity = GetIdentity(string.IsNullOrWhiteSpace(progress.FilePath)
            ? progress.FileName
            : progress.FilePath);
        var key = identity;
        if (_items.TryGetValue(key, out var item))
            return item;

        var planned = _plan.TryGetValue(identity, out var planItem)
            ? planItem
            : new AssetPlanItem(_items.Count + 1, identity, Math.Max(_plan.Count, _items.Count + 1));

        item = new ProjectBuildProgressItem
        {
            Phase = ProjectBuildProgressPhase.GitHubPublish,
            Key = $"github:{planned.Position}:{planned.Identity}",
            Title = progress.FileName,
            Kind = "GitHubAsset",
            Position = planned.Position,
            Total = planned.Total
        };
        _items[key] = item;
        _progress.ItemsPlanned(ProjectBuildProgressPhase.GitHubPublish, [item]);
        return item;
    }

    private static string? BuildDetail(GitHubReleaseAssetProgress progress)
    {
        if (!string.IsNullOrWhiteSpace(progress.Detail))
            return progress.Detail;
        if (progress.TotalBytes <= 0)
            return null;

        return $"{FormatBytes(progress.BytesTransferred)} / {FormatBytes(progress.TotalBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{Math.Max(0, bytes)} B";

        var value = Math.Max(0, bytes) / 1024d;
        if (value < 1024)
            return $"{value:0.##} KB";

        return $"{value / 1024d:0.##} MB";
    }

    private static string GetIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path.Trim();
        }
    }

    private readonly struct AssetPlanItem
    {
        internal AssetPlanItem(int position, string identity, int total = 0)
        {
            Position = position;
            Identity = identity;
            Total = total;
        }

        internal int Position { get; }
        internal string Identity { get; }
        internal int Total { get; }
    }
}
