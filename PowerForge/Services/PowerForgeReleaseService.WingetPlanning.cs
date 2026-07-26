namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static PowerForgeToolGitHubReleaseResult[] BuildToolGitHubReleasePlans(
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseResult result)
    {
        var gitHub = spec.Tools?.GitHub;
        if (gitHub?.Publish != true)
            return [];

        var owner = string.IsNullOrWhiteSpace(gitHub.Owner)
            ? spec.Packages?.GitHubUsername
            : gitHub.Owner!.Trim();
        var repository = string.IsNullOrWhiteSpace(gitHub.Repository)
            ? spec.Packages?.GitHubRepositoryName
            : gitHub.Repository!.Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repository))
            return [];

        var tagTemplate = string.IsNullOrWhiteSpace(gitHub.TagTemplate)
            ? "{Target}-v{Version}"
            : gitHub.TagTemplate!;
        var releaseNameTemplate = string.IsNullOrWhiteSpace(gitHub.ReleaseNameTemplate)
            ? "{Target} {Version}"
            : gitHub.ReleaseNameTemplate!;
        return (result.ReleaseAssetEntries ?? [])
            .Where(static asset =>
                !string.IsNullOrWhiteSpace(asset.Target) &&
                !string.IsNullOrWhiteSpace(asset.Version) &&
                asset.Category is not PowerForgeReleaseAssetCategory.Module and
                    not PowerForgeReleaseAssetCategory.Package)
            .GroupBy(
                static asset => $"{asset.Target}\0{asset.Version}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new PowerForgeToolGitHubReleaseResult
                {
                    Owner = owner!,
                    Repository = repository!,
                    Target = first.Target!,
                    Version = first.Version!,
                    TagName = ApplyGitHubTemplate(
                        tagTemplate,
                        first.Target!,
                        first.Version!,
                        repository!),
                    ReleaseName = ApplyGitHubTemplate(
                        releaseNameTemplate,
                        first.Target!,
                        first.Version!,
                        repository!),
                    AssetPaths = group
                        .Select(static asset => asset.StagedPath ?? asset.Path)
                        .Where(static path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Success = true
                };
            })
            .ToArray();
    }
}
