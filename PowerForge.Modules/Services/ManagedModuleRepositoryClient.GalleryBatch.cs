using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    internal static bool SupportsOfficialGalleryBatch(ManagedModuleRepository repository) =>
        (repository.Kind == ManagedModuleRepositoryKind.NuGetV3 && IsPowerShellGalleryV3Index(repository.Source)) ||
        (repository.Kind == ManagedModuleRepositoryKind.NuGetV2 && IsPowerShellGalleryV2Endpoint(repository.Source));

    internal async Task<IReadOnlyDictionary<string, ManagedModuleVersionInfo>?> TryGetOfficialGalleryLatestBatchAsync(
        ManagedModuleRepository repository,
        IReadOnlyList<string> names,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        if (!SupportsOfficialGalleryBatch(repository) || FindCatalogs(repository).Count != 0)
            return null;

        var source = repository.Kind == ManagedModuleRepositoryKind.NuGetV3
            ? CreatePowerShellGalleryV2Fallback(repository)
            : repository;
        var latestPredicate = includePrerelease ? "IsAbsoluteLatestVersion" : "IsLatestVersion";
        var identifiers = string.Join(" or ", names.Select(static name => $"Id eq '{Uri.EscapeDataString(name.Replace("'", "''"))}'"));
        var uri = new Uri(new Uri(EnsureTrailingSlash(source.Source)),
            $"Packages()?$filter=({identifiers}) and {latestPredicate}&$top={names.Count}&semVerLevel=2.0.0");

        // This optional lookup gets one short attempt. Normal per-name lookups
        // retain the configured retry, timeout, and catalog behavior.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var request = CreateRequest(HttpMethod.Get, uri, credential: null, "application/xml");
            using var response = await SendAttemptAsync(request, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            using var abort = deadline.Token.Register(response.Dispose);
            using var content = await ReadContentStreamAsync(response.Content, deadline.Token).ConfigureAwait(false);
            var document = XDocument.Load(content);
            deadline.Token.ThrowIfCancellationRequested();
            return ReadOfficialGalleryLatestBatch(source, names, latestPredicate, includePrerelease, document);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or XmlException or
                                          OperationCanceledException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Verbose($"PSGallery batch lookup failed. Using individual lookups. {exception.Message}");
            return null;
        }
    }

    private static IReadOnlyDictionary<string, ManagedModuleVersionInfo>? ReadOfficialGalleryLatestBatch(
        ManagedModuleRepository repository,
        IReadOnlyList<string> names,
        string latestPredicate,
        bool includePrerelease,
        XDocument document)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        var feed = document.Root;
        if (feed?.Name != atom + "feed" ||
            feed.Elements(atom + "link").Any(static link => (string?)link.Attribute("rel") == "next"))
            return null;

        var requestedNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var versions = new Dictionary<string, ManagedModuleVersionInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in feed.Elements(atom + "entry"))
        {
            var version = ReadNuGetV2SearchResult(repository, entry, data);
            if (version is null || !requestedNames.Contains(version.Name) || versions.ContainsKey(version.Name))
                return null;

            if (!version.Listed || !ReadNuGetV2Boolean(entry, data, latestPredicate) ||
                !NuGetVersion.TryParse(version.Version, out var parsed) || (!includePrerelease && parsed.IsPrerelease))
                continue;

            versions.Add(version.Name, version);
        }

        return versions;
    }
}
