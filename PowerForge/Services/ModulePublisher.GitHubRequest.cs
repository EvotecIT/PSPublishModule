namespace PowerForge;

public sealed partial class ModulePublisher
{
    /// <summary>Maps a built module's release identity and assets to the shared GitHub publisher.</summary>
    internal static GitHubReleasePublishRequest CreateGitHubReleaseRequest(
        PublishConfiguration publish,
        ModulePipelinePlan plan,
        IReadOnlyList<string> assets,
        IGitHubReleaseProgressReporter? progress = null)
    {
        var tag = GetGitHubTag(publish, plan.ModuleName, plan.ResolvedVersion, plan.PreRelease);
        return new GitHubReleasePublishRequest
        {
            Owner = publish.UserName!.Trim(),
            Repository = string.IsNullOrWhiteSpace(publish.RepositoryName) ? plan.ModuleName : publish.RepositoryName!.Trim(),
            Token = publish.ApiKey,
            TagName = tag,
            Commitish = plan.SourceCommitSha,
            ExpectedTagCommitSha = plan.SourceCommitSha,
            ReleaseName = tag,
            GenerateReleaseNotes = publish.GenerateReleaseNotes,
            IsDraft = false,
            IsPreRelease = !string.IsNullOrWhiteSpace(plan.PreRelease) && !publish.DoNotMarkAsPreRelease,
            ReuseExistingReleaseOnConflict = publish.ReuseExistingRelease,
            ReplaceExistingAssets = publish.ReuseExistingRelease && publish.ReplaceExistingAssets,
            AssetFilePaths = assets,
            Progress = progress
        };
    }
}
