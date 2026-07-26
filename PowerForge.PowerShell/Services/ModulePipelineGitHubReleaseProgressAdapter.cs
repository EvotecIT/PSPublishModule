using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModulePipelineGitHubReleaseProgressAdapter : IGitHubReleaseProgressReporter
{
    private readonly ModulePipelineExecutionSession _session;
    private readonly ModulePipelineStep? _step;
    private readonly Dictionary<string, GitHubReleaseAssetProgress> _assets =
        new(StringComparer.OrdinalIgnoreCase);

    internal ModulePipelineGitHubReleaseProgressAdapter(
        ModulePipelineExecutionSession session,
        ModulePipelineStep? step)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _step = step;
    }

    public void Report(GitHubReleaseAssetProgress progress)
    {
        if (progress is null || string.IsNullOrWhiteSpace(progress.FilePath))
            return;

        _assets[progress.FilePath] = progress;
        var maximum = _assets.Values.Sum(static asset => Math.Max(0, asset.TotalBytes));
        var value = _assets.Values.Sum(GetCompletedBytes);
        var fileName = string.IsNullOrWhiteSpace(progress.FileName)
            ? Path.GetFileName(progress.FilePath)
            : progress.FileName;
        var detail = $"{Math.Max(1, progress.Position)}/{Math.Max(1, progress.TotalAssets)} {fileName}";
        if (progress.TotalBytes > 0)
        {
            detail += $" — {DotNetRepositoryReleaseService.FormatBytes(Math.Max(0, progress.BytesTransferred))} / " +
                      DotNetRepositoryReleaseService.FormatBytes(progress.TotalBytes);
        }

        _session.Progress(_step, value, maximum, detail);
    }

    private static long GetCompletedBytes(GitHubReleaseAssetProgress progress)
        => progress.State is GitHubReleaseAssetProgressState.Uploaded or GitHubReleaseAssetProgressState.Skipped
            ? Math.Max(0, progress.TotalBytes)
            : Math.Max(0, progress.BytesTransferred);
}
