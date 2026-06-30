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
        var documents = await ReadNuGetV2XmlPagesAsync(
                repository,
                BuildNuGetV2FindPackagesByIdUri(repository.Source, packageId),
                credential,
                "VersionQuery",
                $"Unable to query versions for package '{packageId}'.",
                $"Managed module NuGet v2 version query for package '{packageId}' returned malformed XML.",
                cancellationToken)
            .ConfigureAwait(false);

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
        CancellationToken cancellationToken)
    {
        var document = await ReadNuGetV2XmlAsync(
                repository,
                BuildNuGetV2SearchUri(repository.Source, query, includePrerelease, take),
                credential,
                "Search",
                $"Unable to search for '{query}'.",
                $"Managed module NuGet v2 search for '{query}' returned malformed XML.",
                cancellationToken)
            .ConfigureAwait(false);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        return document
            .Descendants(atom + "entry")
            .Select(entry => ReadNuGetV2SearchResult(repository, entry, data))
            .Where(version => version is not null && ManagedModuleSearchMatcher.IsMatch(query, version.Name))
            .Select(static version => version!)
            .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(version => version.Version, ManagedModuleVersionComparer.Instance).Last())
            .OrderBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, take))
            .ToArray();
    }

    private async Task<ManagedModuleVersionInfo?> GetLatestNuGetV2VersionAsync(
        ManagedModuleRepository repository,
        string packageId,
        bool includePrerelease,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var document = await ReadNuGetV2XmlAsync(
                repository,
                BuildNuGetV2LatestPackageUri(repository.Source, packageId, includePrerelease),
                credential,
                "LatestVersionQuery",
                $"Unable to query latest version for package '{packageId}'.",
                $"Managed module NuGet v2 latest-version query for package '{packageId}' returned malformed XML.",
                cancellationToken)
            .ConfigureAwait(false);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        return document
            .Descendants(atom + "entry")
            .Select(entry => ReadNuGetV2SearchResult(repository, entry, data))
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
        return new ManagedModuleVersionInfo
        {
            Name = trimmedId,
            Version = trimmedVersion,
            RepositoryName = repository.Name,
            RepositorySource = repository.Source,
            PackageSource = BuildNuGetV2PackageUri(repository.Source, trimmedId, trimmedVersion).ToString(),
            IsPrerelease = ManagedModuleVersionComparer.IsPrerelease(trimmedVersion),
            License = license,
            RequireLicenseAcceptance = ReadNuGetV2Boolean(entry, data, "RequireLicenseAcceptance")
        };
    }

    private static string? ReadNuGetV2String(XElement entry, XNamespace data, string name)
        => entry.Descendants(data + name).FirstOrDefault()?.Value.Trim();

    private static bool ReadNuGetV2Boolean(XElement entry, XNamespace data, string name)
    {
        var value = ReadNuGetV2String(entry, data, name);
        return bool.TryParse(value, out var parsed) && parsed;
    }

    private static Uri BuildNuGetV2PackageUri(string source, string packageId, string version)
        => new(
            new Uri(EnsureTrailingSlash(source)),
            $"package/{Uri.EscapeDataString(packageId.Trim())}/{Uri.EscapeDataString(version.Trim())}");

    private static Uri BuildNuGetV2FindPackagesByIdUri(string source, string packageId)
    {
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''"));
        return new Uri(new Uri(EnsureTrailingSlash(source)), $"FindPackagesById()?id='{escapedId}'");
    }

    private static Uri BuildNuGetV2SearchUri(string source, string pattern, bool includePrerelease, int take)
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
            $"Packages()?$filter={filter}&$top={Math.Max(1, take)}");
    }

    private static Uri BuildNuGetV2LatestPackageUri(string source, string packageId, bool includePrerelease)
    {
        var escapedId = Uri.EscapeDataString(packageId.Trim().Replace("'", "''"));
        var latestPredicate = includePrerelease ? "IsAbsoluteLatestVersion" : "IsLatestVersion";
        return new Uri(
            new Uri(EnsureTrailingSlash(source)),
            $"FindPackagesById()?id='{escapedId}'&$filter={latestPredicate}&$top=1");
    }

    private static bool ShouldUsePrefixFilter(string pattern)
        => pattern.Trim().EndsWith("*", StringComparison.Ordinal) &&
           !pattern.Trim().StartsWith("*", StringComparison.Ordinal) &&
           pattern.IndexOf('?') < 0;
}
