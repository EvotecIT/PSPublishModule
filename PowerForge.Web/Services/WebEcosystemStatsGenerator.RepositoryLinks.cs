namespace PowerForge.Web;

public static partial class WebEcosystemStatsGenerator
{
    /// <summary>
    /// Removes package project links to repositories in the configured organization
    /// that are absent from the document's public GitHub repository inventory.
    /// </summary>
    /// <param name="document">Current or retained ecosystem metadata to normalize in place.</param>
    /// <param name="configuredOrganization">Current operation's organization, including when a restored snapshot has no inventory.</param>
    /// <param name="publicationInventory">Effective inventory after selecting retained data for a failed source.</param>
    /// <returns>The number of links removed.</returns>
    /// <remarks>
    /// Non-GitHub links and links to other organizations are preserved. Package-only
    /// documents without a GitHub organization are unchanged. An empty public inventory
    /// does not establish that any repository in the configured organization is public.
    /// </remarks>
    public static int NormalizePublicProjectLinks(WebEcosystemStatsDocument document,
        string? configuredOrganization = null, WebEcosystemGitHubStats? publicationInventory = null)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (!string.IsNullOrWhiteSpace(configuredOrganization) &&
            !string.Equals(document.GitHub?.Organization, configuredOrganization.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            var incompatibleInventory = !string.IsNullOrWhiteSpace(document.GitHub?.Organization);
            document.GitHub = new() { Organization = configuredOrganization.Trim() };
            if (incompatibleInventory)
            {
                document.Summary.RepositoryCount = 0;
                document.Summary.GitHubStars = 0;
                document.Summary.GitHubForks = 0;
            }
        }
        var inventory = publicationInventory ?? document.GitHub;
        var configuredInventory = !string.IsNullOrWhiteSpace(configuredOrganization) &&
            !string.Equals(configuredOrganization.Trim(), inventory?.Organization, StringComparison.OrdinalIgnoreCase)
            ? new WebEcosystemGitHubStats { Organization = configuredOrganization.Trim() } : null;
        bool Withhold(string? url) => url is not null &&
            (NormalizePublicProjectLink(url, inventory) is null ||
             NormalizePublicProjectLink(url, configuredInventory) is null);

        // Preserve matching keys without publishing repository owner/name strings.
        var packages = document.NuGet?.Items ?? new List<WebEcosystemNuGetPackage>();
        var removed = 0;
        foreach (var package in packages)
        {
            if (!Withhold(package.ProjectUrl)) continue;
            if (TryReadGitHubRepository(package.ProjectUrl, out var repository))
            {
                package.ProjectRepositoryKey = GetPackageRepositoryKey(repository);
                package.ProjectRepositoryNameKey = GetPackageRepositoryKey(repository[(repository.IndexOf('/') + 1)..]);
            }
            package.ProjectUrl = null;
            removed++;
        }
        foreach (var module in document.PowerShellGallery?.Modules ?? new List<WebEcosystemPowerShellGalleryModule>())
        {
            if (!Withhold(module.ProjectUrl)) continue;
            module.ProjectUrl = null;
            removed++;
        }
        return removed;
    }

    /// <summary>Builds a stable case-insensitive matching key for a repository identity or repository-name alias.</summary>
    /// <param name="repositoryIdentifier">Owner/repository identity, or a short repository name.</param>
    /// <returns>A SHA-256 key for matching metadata without emitting the input name as a URL or string.</returns>
    public static string GetPackageRepositoryKey(string repositoryIdentifier)
    {
        if (repositoryIdentifier is null) throw new ArgumentNullException(nameof(repositoryIdentifier));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(repositoryIdentifier.Trim().ToLowerInvariant())));
    }

    /// <summary>Retains a package project URL only when its organization repository appears in the public inventory.</summary>
    /// <param name="projectUrl">Package project URL, including a repository subpath if present.</param>
    /// <param name="inventory">Public repository inventory; a missing organization leaves the URL unchanged.</param>
    /// <returns>The original URL, or null for an unlisted repository in the inventory organization.</returns>
    public static string? NormalizePublicProjectLink(string? projectUrl, WebEcosystemGitHubStats? inventory)
    {
        if (string.IsNullOrWhiteSpace(inventory?.Organization) ||
            !TryReadGitHubRepository(projectUrl, out var fullName) ||
            !fullName.StartsWith(inventory.Organization.Trim() + "/", StringComparison.OrdinalIgnoreCase))
            return projectUrl;

        return inventory.Repositories.Any(item => fullName.Equals(item.FullName, StringComparison.OrdinalIgnoreCase))
            ? projectUrl : null;
    }

    private static bool TryReadGitHubRepository(string? projectUrl, out string fullName)
    {
        fullName = string.Empty;
        if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out var uri) ||
            uri.Host.ToLowerInvariant() is not ("github.com" or "www.github.com" or "api.github.com" or "raw.githubusercontent.com"))
            return false;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var offset = uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (segments.Length < offset + 2 ||
            (offset == 1 && !segments[0].Equals("repos", StringComparison.OrdinalIgnoreCase)))
            return false;

        var repository = Uri.UnescapeDataString(segments[offset + 1]);
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];
        fullName = $"{Uri.UnescapeDataString(segments[offset])}/{repository}";
        return true;
    }
}
