using System.Net.Http;
using System.Xml.Linq;
using System.Globalization;

namespace PowerForge;

/// <summary>
/// Queries the PowerShell Gallery OData feed for package versions, including unlisted entries.
/// </summary>
public sealed class PowerShellGalleryVersionFeedClient
{
    private const string FindPackagesByIdTemplate = "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='{0}'";
    private const string ExactPackageTemplate = "https://www.powershellgallery.com/api/v2/Packages(Id='{0}',Version='{1}')";
    private const string UnlistedPublishedMarker = "1900-01-01T00:00:00";
    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient _client;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new client.
    /// </summary>
    public PowerShellGalleryVersionFeedClient(ILogger logger, HttpClient? client = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? SharedClient;
    }

    /// <summary>
    /// Returns all versions exposed by the raw PowerShell Gallery package feed, including unlisted versions.
    /// </summary>
    public IReadOnlyList<PowerShellGalleryPackageVersion> GetVersions(
        string packageId,
        bool includePrerelease,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("PackageId is required.", nameof(packageId));

        var versions = new List<PowerShellGalleryPackageVersion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            FindPackagesByIdTemplate,
            Uri.EscapeDataString(packageId.Trim()));
        var pageCount = 0;

        using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        var token = cts?.Token ?? CancellationToken.None;

        while (!string.IsNullOrWhiteSpace(current))
        {
            var currentUrl = current!;
            pageCount++;
            if (pageCount > 100)
                throw new InvalidOperationException($"PowerShell Gallery version query for '{packageId}' exceeded the page limit.");

            using var response = _client.GetAsync(currentUrl, token).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var document = XDocument.Parse(xml);

            foreach (var item in ParseEntries(document, includePrerelease))
            {
                if (seen.Add(item.VersionText))
                    versions.Add(item);
            }

            current = ResolveNextLink(document, currentUrl);
        }

        return versions;
    }

    /// <summary>
    /// Checks whether the exact package/version exists in the gallery metadata endpoint,
    /// including versions that are unlisted or removed from the public feed listing.
    /// </summary>
    public bool VersionExists(
        string packageId,
        string version,
        TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("PackageId is required.", nameof(packageId));
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : null;
        var token = cts?.Token ?? CancellationToken.None;
        var requestUri = string.Format(
            CultureInfo.InvariantCulture,
            ExactPackageTemplate,
            Uri.EscapeDataString(packageId.Trim()),
            Uri.EscapeDataString(version.Trim()));

        try
        {
            using var response = _client.GetAsync(requestUri, token).GetAwaiter().GetResult();
            return response.IsSuccessStatusCode;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static IEnumerable<PowerShellGalleryPackageVersion> ParseEntries(XDocument document, bool includePrerelease)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace d = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        XNamespace m = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";

        foreach (var entry in document.Root?.Elements(atom + "entry") ?? Array.Empty<XElement>())
        {
            var properties = entry.Element(m + "properties");
            if (properties is null)
                continue;

            var versionText = properties.Element(d + "Version")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(versionText))
                continue;
            var resolvedVersionText = versionText!;

            var isPrereleaseText = properties.Element(d + "IsPrerelease")?.Value?.Trim();
            var isPrerelease =
                string.Equals(isPrereleaseText, "true", StringComparison.OrdinalIgnoreCase) ||
                resolvedVersionText.IndexOf('-') >= 0;

            if (isPrerelease && !includePrerelease)
                continue;

            var publishedText = properties.Element(d + "Published")?.Value?.Trim();
            var isListed = !string.Equals(publishedText, UnlistedPublishedMarker, StringComparison.OrdinalIgnoreCase);

            yield return new PowerShellGalleryPackageVersion(resolvedVersionText, isListed, isPrerelease);
        }
    }

    private static string? ResolveNextLink(XDocument document, string currentUrl)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var next = document.Root?
            .Elements(atom + "link")
            .FirstOrDefault(link => string.Equals(link.Attribute("rel")?.Value, "next", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("href")
            ?.Value;

        if (string.IsNullOrWhiteSpace(next))
            return null;

        if (Uri.TryCreate(next, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (Uri.TryCreate(new Uri(currentUrl, UriKind.Absolute), next, out var combined))
            return combined.ToString();

        return next;
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge/1.0");
        return client;
    }
}

/// <summary>
/// One package version returned by the raw PowerShell Gallery feed.
/// </summary>
public sealed class PowerShellGalleryPackageVersion
{
    /// <summary>
    /// Creates a new version entry.
    /// </summary>
    public PowerShellGalleryPackageVersion(string versionText, bool isListed, bool isPrerelease)
    {
        VersionText = versionText;
        IsListed = isListed;
        IsPrerelease = isPrerelease;
    }

    /// <summary>Raw package version text from the gallery feed.</summary>
    public string VersionText { get; }

    /// <summary>Whether the package version is listed on the gallery.</summary>
    public bool IsListed { get; }

    /// <summary>Whether the package version is a prerelease.</summary>
    public bool IsPrerelease { get; }
}
