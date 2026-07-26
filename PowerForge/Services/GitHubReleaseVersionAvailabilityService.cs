using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace PowerForge;

/// <summary>Resolves a collision-free version for a GitHub release tag.</summary>
public sealed class GitHubReleaseVersionAvailabilityService
{
    private const int MaximumCandidateCount = 24;
    private readonly ILogger _logger;
    private readonly Func<string, string, string, string, GitHubReleaseVersionOccupancy> _probe;

    /// <summary>Creates a GitHub release version availability service.</summary>
    public GitHubReleaseVersionAvailabilityService(ILogger logger)
        : this(logger, probe: null)
    {
    }

    internal GitHubReleaseVersionAvailabilityService(
        ILogger logger,
        Func<string, string, string, string, GitHubReleaseVersionOccupancy>? probe = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _probe = probe ?? Probe;
    }

    /// <summary>
    /// Returns the first candidate whose generated GitHub tag is not occupied, or throws for an exact collision.
    /// </summary>
    public string EnsureAvailable(
        string expectedVersion,
        string candidateVersion,
        string owner,
        string repository,
        string token,
        Func<string, string> buildTag,
        bool reuseExistingRelease)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new ArgumentException("Expected version is required.", nameof(expectedVersion));
        if (string.IsNullOrWhiteSpace(candidateVersion))
            throw new ArgumentException("Candidate version is required.", nameof(candidateVersion));
        if (buildTag is null)
            throw new ArgumentNullException(nameof(buildTag));

        var candidate = candidateVersion.Trim();
        var firstCandidate = candidate;
        for (var index = 0; index < MaximumCandidateCount; index++)
        {
            var tag = buildTag(candidate);
            var occupancy = _probe(owner, repository, token, tag);
            if (!occupancy.Occupied)
                return candidate;

            if (reuseExistingRelease)
            {
                _logger.Warn(
                    $"GitHub release recovery mode will reuse occupied tag '{tag}' " +
                    $"({occupancy.Describe()}).");
                return candidate;
            }

            if (Version.TryParse(expectedVersion, out _))
            {
                throw new InvalidOperationException(
                    $"GitHub tag '{tag}' already exists ({occupancy.Describe()}) for exact release version '{candidate}'. " +
                    "Choose a new version, or explicitly enable ReuseExistingRelease for a deliberate recovery run.");
            }

            if (!TrySplitVersion(candidate, out var numericVersion, out var suffix))
            {
                throw new InvalidOperationException(
                    $"GitHub tag '{tag}' is occupied, but candidate version '{candidate}' could not be stepped using expected pattern '{expectedVersion}'.");
            }

            var next = VersionPatternStepper.Step(expectedVersion, numericVersion);
            candidate = next + suffix;
            _logger.Info(
                $"GitHub tag '{tag}' is occupied ({occupancy.Describe()}); trying release version '{candidate}'.");
        }

        throw new InvalidOperationException(
            $"GitHub reports that all {MaximumCandidateCount} candidate versions starting at '{firstCandidate}' are occupied for '{owner}/{repository}'. " +
            "No free unified release version was selected.");
    }

    internal static GitHubReleaseVersionOccupancy Probe(
        string owner,
        string repository,
        string token,
        string tag)
    {
        using var client = CreateClient();
        var escapedTag = Uri.EscapeDataString(tag);
        var releaseUri = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/releases/tags/{escapedTag}");
        var releaseStatus = SendProbe(client, releaseUri, token, tag, "release");
        if (releaseStatus == HttpStatusCode.OK)
            return new GitHubReleaseVersionOccupancy { ReleaseExists = true };

        var tagUri = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repository)}/git/ref/tags/{escapedTag}");
        var tagStatus = SendProbe(client, tagUri, token, tag, "tag");
        return new GitHubReleaseVersionOccupancy
        {
            TagExists = tagStatus == HttpStatusCode.OK
        };
    }

    private static HttpStatusCode SendProbe(
        HttpClient client,
        Uri uri,
        string token,
        string tag,
        string resource)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound)
            return response.StatusCode;

        var responseText = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        throw new InvalidOperationException(
            $"GitHub {resource} availability check failed for '{tag}' ({(int)response.StatusCode} {response.ReasonPhrase}). " +
            TrimForMessage(responseText));
    }

    private static bool TrySplitVersion(
        string candidate,
        out Version numericVersion,
        out string suffix)
    {
        var separator = candidate.IndexOf('-');
        var numeric = separator >= 0 ? candidate.Substring(0, separator) : candidate;
        suffix = separator >= 0 ? candidate.Substring(separator) : string.Empty;
        return Version.TryParse(numeric, out numericVersion!);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge", "1.0"));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static string TrimForMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text!.Trim();
        return trimmed.Length > 1000 ? trimmed.Substring(0, 1000) + "..." : trimmed;
    }
}

internal sealed class GitHubReleaseVersionOccupancy
{
    internal bool ReleaseExists { get; set; }

    internal bool TagExists { get; set; }

    internal bool Occupied => ReleaseExists || TagExists;

    internal string Describe()
        => ReleaseExists && TagExists
            ? "release and tag already exist"
            : ReleaseExists
                ? "release already exists"
                : "tag already exists";
}
