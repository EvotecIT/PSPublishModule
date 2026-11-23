namespace PowerForge;

internal static class AzureArtifactsModuleEndpoints
{
    internal static AzureArtifactsRepositoryEndpoint Create(
        string organization,
        string? project,
        string feed,
        string? repositoryName = null)
    {
        var normalizedOrganization = NormalizeRequiredSegment(organization, nameof(organization));
        var normalizedFeed = NormalizeRequiredSegment(feed, nameof(feed));
        var normalizedProject = NormalizeOptionalSegment(project);
        var resolvedName = string.IsNullOrWhiteSpace(repositoryName)
            ? normalizedFeed
            : repositoryName!.Trim();

        if (string.Equals(resolvedName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Azure Artifacts repository name cannot resolve to 'PSGallery'.", nameof(repositoryName));

        var baseUri = string.IsNullOrWhiteSpace(normalizedProject)
            ? $"https://pkgs.dev.azure.com/{Uri.EscapeDataString(normalizedOrganization)}/_packaging/{Uri.EscapeDataString(normalizedFeed)}/nuget"
            : $"https://pkgs.dev.azure.com/{Uri.EscapeDataString(normalizedOrganization)}/{Uri.EscapeDataString(normalizedProject)}/_packaging/{Uri.EscapeDataString(normalizedFeed)}/nuget";

        return new AzureArtifactsRepositoryEndpoint(
            repositoryName: resolvedName,
            organization: normalizedOrganization,
            project: normalizedProject,
            feed: normalizedFeed,
            powerShellGetSourceUri: baseUri + "/v2",
            powerShellGetPublishUri: baseUri + "/v2",
            psResourceGetUri: baseUri + "/v3/index.json");
    }

    private static string NormalizeRequiredSegment(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", paramName);

        return value.Trim().Trim('/');
    }

    private static string? NormalizeOptionalSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value!.Trim().Trim('/');
    }
}
