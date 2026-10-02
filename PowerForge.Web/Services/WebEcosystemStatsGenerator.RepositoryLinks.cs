namespace PowerForge.Web;

public static partial class WebEcosystemStatsGenerator
{
    /// <summary>
    /// Removes package project links to repositories in the configured organization
    /// that are absent from the document's public GitHub repository inventory.
    /// </summary>
    /// <param name="document">Current or retained ecosystem metadata to normalize in place.</param>
    /// <returns>The number of links removed.</returns>
    /// <remarks>
    /// Non-GitHub links and links to other organizations are preserved. Package-only
    /// documents without a GitHub organization are unchanged. An empty public inventory
    /// does not establish that any repository in the configured organization is public.
    /// </remarks>
    public static int NormalizePublicProjectLinks(WebEcosystemStatsDocument document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var removed = 0;
        foreach (var package in document.NuGet?.Items ?? new List<WebEcosystemNuGetPackage>())
        {
            if (package.ProjectUrl is null || NormalizePublicProjectLink(package.ProjectUrl, document.GitHub) is not null) continue;
            package.ProjectUrl = null;
            removed++;
        }
        foreach (var module in document.PowerShellGallery?.Modules ?? new List<WebEcosystemPowerShellGalleryModule>())
        {
            if (module.ProjectUrl is null || NormalizePublicProjectLink(module.ProjectUrl, document.GitHub) is not null) continue;
            module.ProjectUrl = null;
            removed++;
        }
        return removed;
    }

    /// <summary>Retains a package project URL only when its organization repository appears in the public inventory.</summary>
    /// <param name="projectUrl">Package project URL, including a repository subpath if present.</param>
    /// <param name="inventory">Public repository inventory; a missing organization leaves the URL unchanged.</param>
    /// <returns>The original URL, or null for an unlisted repository in the inventory organization.</returns>
    public static string? NormalizePublicProjectLink(string? projectUrl, WebEcosystemGitHubStats? inventory)
    {
        if (string.IsNullOrWhiteSpace(inventory?.Organization) ||
            !Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) ||
            uri.Host.ToLowerInvariant() is not ("github.com" or "www.github.com" or "api.github.com" or "raw.githubusercontent.com"))
            return projectUrl;

        var organization = inventory.Organization.Trim();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var offset = uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (segments.Length < offset + 2 ||
            (offset == 1 && !segments[0].Equals("repos", StringComparison.OrdinalIgnoreCase)) ||
            !Uri.UnescapeDataString(segments[offset]).Equals(organization, StringComparison.OrdinalIgnoreCase))
            return projectUrl;

        var repository = Uri.UnescapeDataString(segments[offset + 1]);
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];
        var fullName = $"{organization}/{repository}";
        return inventory.Repositories.Any(item => fullName.Equals(item.FullName, StringComparison.OrdinalIgnoreCase))
            ? projectUrl : null;
    }
}
