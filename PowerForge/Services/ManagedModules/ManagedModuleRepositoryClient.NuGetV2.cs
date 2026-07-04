using System.Net.Http;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> GetNuGetV2VersionsAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<XDocument> documents;
        try
        {
            documents = await ReadNuGetV2XmlPagesAsync(
                    repository,
                    BuildNuGetV2FindPackagesByIdUri(repository.Source, packageId),
                    credential,
                    "VersionQuery",
                    $"Unable to query versions for package '{packageId}'.",
                    $"Managed module NuGet v2 version query for package '{packageId}' returned malformed XML.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ManagedModuleRepositoryException ex) when (IsRepositoryPackageNotFound(ex))
        {
            return Array.Empty<ManagedModuleVersionInfo>();
        }

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        return documents
            .SelectMany(document => document.Descendants(atom + "entry"))
            .Select(entry => ReadNuGetV2SearchResult(repository, entry, data, packageId))
            .Where(version => version is not null && (includePrerelease || !version!.IsPrerelease))
            .Select(static version => version!)
            .GroupBy(version => version.Version, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance)
            .ToArray();
    }

    private async Task<IReadOnlyList<ManagedModuleVersionInfo>> SearchNuGetV2PackagesAsync(
        ManagedModuleRepository repository,
        string query,
        bool includePrerelease,
        RepositoryCredential? credential,
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        var pageSize = Math.Max(1, take);
        var matchingSkip = Math.Max(0, skip);
        var matches = new List<ManagedModuleVersionInfo>();
        var serverSkip = 0;

        while (matches.Count < pageSize)
        {
            var document = await ReadNuGetV2XmlAsync(
                    repository,
                    BuildNuGetV2SearchUri(repository.Source, query, includePrerelease, pageSize, serverSkip),
                    credential,
                    "Search",
                    $"Unable to search for '{query}'.",
                    $"Managed module NuGet v2 search for '{query}' returned malformed XML.",
                    cancellationToken)
                .ConfigureAwait(false);

            var entries = document.Descendants(atom + "entry").ToArray();
            if (entries.Length == 0)
                break;

            var pageMatches = entries
                .Select(entry => ReadNuGetV2SearchResult(repository, entry, data))
                .Where(version => version is not null && ManagedModuleSearchMatcher.IsMatch(query, version.Name))
                .Select(static version => version!)
                .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance).Last())
                .OrderBy(version => version.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var match in pageMatches)
            {
                if (matchingSkip > 0)
                {
                    matchingSkip--;
                    continue;
                }

                matches.Add(match);
                if (matches.Count >= pageSize)
                    break;
            }

            if (entries.Length < pageSize)
                break;

            serverSkip += entries.Length;
        }

        return matches;
    }

    private async Task<ManagedModuleVersionInfo?> GetLatestNuGetV2VersionAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        XDocument document;
        try
        {
            document = await ReadNuGetV2XmlAsync(
                    repository,
                    BuildNuGetV2LatestPackageUri(repository.Source, packageId, includePrerelease),
                    credential,
                    "LatestVersionQuery",
                    $"Unable to query latest version for package '{packageId}'.",
                    $"Managed module NuGet v2 latest-version query for package '{packageId}' returned malformed XML.",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ManagedModuleRepositoryException ex) when (IsRepositoryPackageNotFound(ex))
        {
            return null;
        }

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        return document
            .Descendants(atom + "entry")
            .Select(entry => ReadNuGetV2SearchResult(repository, entry, data, packageId))
            .Where(version => version is not null && version.Name.Equals(packageId, StringComparison.OrdinalIgnoreCase))
            .Select(static version => version!)
            .OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance)
            .LastOrDefault();
    }

    private async Task<ManagedModuleDownloadResult> DownloadNuGetV2PackageAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        if (ShouldUsePowerShellGalleryCdn(repository))
        {
            return await DownloadPowerShellGalleryPackageWithCdnFallbackAsync(
                    repository,
                    packageId,
                    version,
                    destinationDirectory,
                    credential,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return await DownloadNuGetV2PackageDirectAsync(repository, packageId, version, destinationDirectory, credential, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ManagedModuleDownloadResult> DownloadNuGetV2PackageDirectAsync(
        ManagedModuleRepository repository,
        string packageId,
        string version,
        string destinationDirectory,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var packageUri = BuildNuGetV2PackageUri(repository.Source, packageId, version);
        var destinationPath = BuildDestinationPath(destinationDirectory, repository, packageId, version);
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, packageUri, credential, "application/octet-stream"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, "Download", response.StatusCode, $"Unable to download package '{packageId}' version '{version}'.");

        PackageCopyResult packageCopy;
        using (var source = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false))
        {
            packageCopy = await CopyPackageStreamWithHashAsync(source, destinationPath, _options.MaxPackageBytes, cancellationToken).ConfigureAwait(false);
        }

        return new ManagedModuleDownloadResult
        {
            Name = packageId,
            Version = version,
            RepositoryName = repository.Name,
            Source = packageUri.ToString(),
            PackagePath = destinationPath,
            BytesWritten = packageCopy.BytesWritten,
            PackageSha256 = packageCopy.Sha256,
            Metadata = ReadDownloadedPackageMetadata(packageId, version, destinationPath)
        };
    }

    private async Task<XDocument> ReadNuGetV2XmlAsync(
        ManagedModuleRepository repository,
        Uri uri,
        RepositoryCredential? credential,
        string operation,
        string failureMessage,
        string malformedMessage,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithPolicyAsync(
            () => CreateRequest(HttpMethod.Get, uri, credential, "application/xml"),
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateRepositoryHttpException(repository, operation, response.StatusCode, failureMessage);

        try
        {
            using var stream = await ReadContentStreamAsync(response.Content, cancellationToken).ConfigureAwait(false);
            return XDocument.Load(stream);
        }
        catch (Exception ex) when (ex is InvalidOperationException || ex is System.Xml.XmlException)
        {
            throw CreateRepositoryContractException(repository, operation, malformedMessage);
        }
    }

    private async Task<IReadOnlyList<XDocument>> ReadNuGetV2XmlPagesAsync(
        ManagedModuleRepository repository,
        Uri uri,
        RepositoryCredential? credential,
        string operation,
        string failureMessage,
        string malformedMessage,
        CancellationToken cancellationToken)
    {
        const int MaxPages = 100;
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var documents = new List<XDocument>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = uri;

        for (var page = 0; page < MaxPages; page++)
        {
            if (!visited.Add(current.AbsoluteUri))
                throw CreateRepositoryContractException(repository, operation, $"Managed module NuGet v2 query for '{uri}' returned a repeated next page link.");

            var document = await ReadNuGetV2XmlAsync(
                    repository,
                    current,
                    credential,
                    operation,
                    failureMessage,
                    malformedMessage,
                    cancellationToken)
                .ConfigureAwait(false);
            documents.Add(document);

            var next = document
                .Descendants(atom + "link")
                .FirstOrDefault(link => string.Equals((string?)link.Attribute("rel"), "next", StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")
                ?.Value;
            if (string.IsNullOrWhiteSpace(next))
                return documents;

            current = Uri.TryCreate(next, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(current, next);
        }

        throw CreateRepositoryContractException(repository, operation, $"Managed module NuGet v2 query for '{uri}' exceeded the page limit.");
    }

    private static ManagedModuleVersionInfo? ReadNuGetV2SearchResult(
        ManagedModuleRepository repository,
        XElement entry,
        XNamespace data,
        string? fallbackId = null)
    {
        var id = entry.Descendants(data + "Id").FirstOrDefault()?.Value ?? fallbackId;
        var version = entry.Descendants(data + "Version").FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
            return null;

        var trimmedId = id!.Trim();
        var trimmedVersion = version!.Trim();
        var license = ReadNuGetV2String(entry, data, "LicenseExpression") ??
                      ReadNuGetV2String(entry, data, "LicenseUrl");
        var dependencies = ReadNuGetV2Dependencies(entry, data);
        var tags = SplitTags(ReadNuGetV2String(entry, data, "Tags"));
        return new ManagedModuleVersionInfo
        {
            Name = trimmedId,
            Version = trimmedVersion,
            RepositoryName = repository.Name,
            RepositorySource = repository.Source,
            PackageSource = BuildNuGetV2PackageUri(repository.Source, trimmedId, trimmedVersion).ToString(),
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(trimmedVersion),
            Listed = ReadNuGetV2Listed(entry, data),
            License = license,
            RequireLicenseAcceptance = ReadNuGetV2Boolean(entry, data, "RequireLicenseAcceptance"),
            Dependencies = dependencies,
            Tags = tags
        };
    }

    private static string? ReadNuGetV2String(XElement entry, XNamespace data, string name)
        => entry.Descendants(data + name).FirstOrDefault()?.Value.Trim();

    private static bool ReadNuGetV2Boolean(XElement entry, XNamespace data, string name)
    {
        var value = ReadNuGetV2String(entry, data, name);
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static bool ReadNuGetV2Listed(XElement entry, XNamespace data)
    {
        var value = ReadNuGetV2String(entry, data, "Listed");
        return !bool.TryParse(value, out var parsed) || parsed;
    }

    private static IReadOnlyList<ManagedModuleDependencyInfo> ReadNuGetV2Dependencies(XElement entry, XNamespace data)
    {
        var value = ReadNuGetV2String(entry, data, "Dependencies");
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<ManagedModuleDependencyInfo>();

        var dependencies = new List<ManagedModuleDependencyInfo>();
        foreach (var dependencyText in value!.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = dependencyText.Split(new[] { ':' }, 3);
            if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            dependencies.Add(new ManagedModuleDependencyInfo
            {
                Id = parts[0].Trim(),
                VersionRange = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : null,
                TargetFramework = parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2].Trim() : null
            });
        }

        return dependencies.Count == 0 ? Array.Empty<ManagedModuleDependencyInfo>() : dependencies;
    }

    private static Uri BuildNuGetV2PackageUri(string source, string packageId, string version)
        => new(
            new Uri(EnsureTrailingSlash(source)),
            $"package/{Uri.EscapeDataString(packageId.Trim())}/{Uri.EscapeDataString(version.Trim())}");

    private static Uri BuildNuGetV2FindPackagesByIdUri(string source, string packageId)
    {
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''"));
        return new Uri(new Uri(EnsureTrailingSlash(source)), $"FindPackagesById()?id='{escapedId}'&semVerLevel=2.0.0");
    }

    private static Uri BuildNuGetV2SearchUri(string source, string pattern, bool includePrerelease, int take, int skip)
    {
        var searchText = ManagedModuleSearchMatcher.ToSearchText(pattern);
        var escapedSearch = Uri.EscapeDataString(searchText.Trim().Replace("'", "''"));
        var filter = ShouldUsePrefixFilter(pattern)
            ? $"startswith(Id,'{escapedSearch}')"
            : $"substringof('{escapedSearch}',Id)";
        filter += includePrerelease
            ? " and IsAbsoluteLatestVersion"
            : " and IsLatestVersion";

        return new Uri(
            new Uri(EnsureTrailingSlash(source)),
            $"Packages()?$filter={filter}&$top={Math.Max(1, take)}&$skip={Math.Max(0, skip)}&semVerLevel=2.0.0");
    }

    private static Uri BuildNuGetV2LatestPackageUri(string source, string packageId, bool includePrerelease)
    {
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''"));
        var latestPredicate = includePrerelease ? "IsAbsoluteLatestVersion" : "IsLatestVersion";
        return new Uri(
            new Uri(EnsureTrailingSlash(source)),
            $"Packages()?$filter=Id eq '{escapedId}' and {latestPredicate}&$top=1&semVerLevel=2.0.0");
    }

    private static bool ShouldUsePrefixFilter(string pattern)
        => pattern.Trim().EndsWith("*", StringComparison.Ordinal) &&
           !pattern.Trim().StartsWith("*", StringComparison.Ordinal) &&
           pattern.IndexOf('?') < 0;
}
