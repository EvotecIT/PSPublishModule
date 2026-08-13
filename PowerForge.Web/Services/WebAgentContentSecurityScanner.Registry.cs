using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

public sealed partial class WebAgentContentSecurityScanner
{
    private bool VerifyPackage(
        WebAgentPackageReference package,
        WebAgentContentSecurityOptions options,
        WebPublicationCatalog? catalog,
        IDictionary<string, PackageVerificationOutcome> cache,
        List<WebAgentContentSecurityFinding> findings,
        CancellationToken networkBudget)
    {
        if (!IsValidPackageId(package.Ecosystem, package.Id))
        {
            AddPackageFinding(findings, package, "PFAGENT.PACKAGE.INVALID_ID",
                "Package identifier contains characters or a shape not accepted by the selected public registry.");
            return false;
        }

        var selector = $"{package.Ecosystem}:{package.Id}";
        var ownerRequired = MatchesAnySelector(selector, options.RequireOwnerVerification) &&
                            !MatchesAnySelector(selector, options.RegistryVerifiedPackages);
        if (ownerRequired)
        {
            if (catalog is null)
            {
                AddPackageFinding(findings, package, "PFAGENT.PACKAGE.OWNER_CATALOG_REQUIRED",
                    "Package requires owner verification, but no valid owner-scoped publication catalog is available.");
                return false;
            }

            var expectedOwner = package.Ecosystem switch
            {
                "nuget" => options.NuGetOwner,
                "powershellgallery" => options.PowerShellGalleryOwner,
                _ => null
            };
            if (string.IsNullOrWhiteSpace(expectedOwner))
            {
                AddPackageFinding(findings, package, "PFAGENT.PACKAGE.OWNER_REQUIRED",
                    $"Package selector '{selector}' requires owner verification, but this ecosystem has no configured expected owner.");
                return false;
            }
            if (!HasExactRegistryVersion(package.Ecosystem, package.Version))
            {
                AddPackageFinding(findings, package, "PFAGENT.PACKAGE.EXACT_VERSION_REQUIRED",
                    "Owner-verified installation commands must pin an exact package version.");
                return false;
            }
            bool catalogContains;
            try
            {
                catalogContains = catalog.ContainsExactOwnedPackage(
                    package.Ecosystem,
                    package.Id,
                    package.Version,
                    expectedOwner);
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                AddPackageFinding(findings, package, "PFAGENT.PACKAGE.INVALID_OWNER_CATALOG", ex.Message);
                return false;
            }
            if (!catalogContains)
            {
                AddPackageFinding(findings, package, "PFAGENT.PACKAGE.OWNER_MISMATCH",
                    $"Package '{package.Id}' version '{package.Version}' is not present in the owner-scoped '{expectedOwner}' publication catalog.");
                return false;
            }
            return true;
        }

        var cacheKey = $"{package.Ecosystem}|{package.Id}|{package.Version}";
        if (!cache.TryGetValue(cacheKey, out var outcome))
        {
            outcome = VerifyRegistryPackage(package, options, networkBudget);
            cache[cacheKey] = outcome;
        }

        if (!outcome.Success)
        {
            AddPackageFinding(findings, package, outcome.Code, outcome.Message);
            return false;
        }
        return true;
    }

    private PackageVerificationOutcome VerifyRegistryPackage(
        WebAgentPackageReference package,
        WebAgentContentSecurityOptions options,
        CancellationToken networkBudget)
    {
        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(networkBudget);
            cancellation.CancelAfter(TimeSpan.FromSeconds(options.RequestTimeoutSeconds));
            return package.Ecosystem switch
            {
                "nuget" => VerifyJsonVersions(
                    "nuget",
                    $"https://api.nuget.org/v3-flatcontainer/{Uri.EscapeDataString(package.Id.ToLowerInvariant())}/index.json",
                    package.Version,
                    static root => root.ValueKind == JsonValueKind.Object &&
                                   root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array
                        ? versions.EnumerateArray()
                            .Where(static value => value.ValueKind == JsonValueKind.String)
                            .Select(static value => value.GetString())
                            .Where(static value => !string.IsNullOrWhiteSpace(value))!
                        : Enumerable.Empty<string?>(),
                    options.MaxRegistryResponseBytes,
                    cancellation.Token),
                "npm" => VerifyJsonVersions(
                    "npm",
                    $"https://registry.npmjs.org/{Uri.EscapeDataString(package.Id)}",
                    package.Version,
                    static root => root.ValueKind == JsonValueKind.Object &&
                                   root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Object
                        ? versions.EnumerateObject().Select(static property => property.Name)
                        : Enumerable.Empty<string>(),
                    options.MaxRegistryResponseBytes,
                    cancellation.Token),
                "pypi" => VerifyJsonVersions(
                    "pypi",
                    $"https://pypi.org/pypi/{Uri.EscapeDataString(package.Id)}/json",
                    package.Version,
                    static root => root.ValueKind == JsonValueKind.Object &&
                                   root.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Object
                        ? releases.EnumerateObject().Select(static property => property.Name)
                        : Enumerable.Empty<string>(),
                    options.MaxRegistryResponseBytes,
                    cancellation.Token),
                "crates" => VerifyJsonVersions(
                    "crates",
                    $"https://crates.io/api/v1/crates/{Uri.EscapeDataString(package.Id)}",
                    package.Version,
                    static root => root.ValueKind == JsonValueKind.Object &&
                                   root.TryGetProperty("versions", out var versions) && versions.ValueKind == JsonValueKind.Array
                        ? versions.EnumerateArray()
                            .Where(static value => value.ValueKind == JsonValueKind.Object &&
                                                   value.TryGetProperty("num", out var number) &&
                                                   number.ValueKind == JsonValueKind.String)
                            .Select(static value => value.GetProperty("num").GetString())
                            .Where(static value => !string.IsNullOrWhiteSpace(value))!
                        : Enumerable.Empty<string?>(),
                    options.MaxRegistryResponseBytes,
                    cancellation.Token),
                "rubygems" => VerifyRubyGems(package, options.MaxRegistryResponseBytes, cancellation.Token),
                "packagist" => VerifyPackagist(package, options.MaxRegistryResponseBytes, cancellation.Token),
                "powershellgallery" => VerifyPowerShellGallery(package, options.MaxRegistryResponseBytes, cancellation.Token),
                _ => new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.UNSUPPORTED_ECOSYSTEM",
                    $"Package ecosystem '{package.Ecosystem}' is not supported.")
            };
        }
        catch (OperationCanceledException)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_TIMEOUT",
                $"Registry verification timed out for '{package.Id}'.");
        }
        catch (HttpRequestException ex)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_UNAVAILABLE",
                $"Registry verification failed for '{package.Id}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_INVALID_RESPONSE",
                $"Registry returned invalid JSON for '{package.Id}': {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_INVALID_RESPONSE",
                $"Registry returned an unexpected response shape for '{package.Id}': {ex.Message}");
        }
        catch (XmlException ex)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_INVALID_RESPONSE",
                $"Registry returned invalid XML for '{package.Id}': {ex.Message}");
        }
        catch (RegistryResponseTooLargeException ex)
        {
            return new PackageVerificationOutcome(false, "PFAGENT.PACKAGE.REGISTRY_RESPONSE_TOO_LARGE", ex.Message);
        }
    }

    private PackageVerificationOutcome VerifyJsonVersions(
        string ecosystem,
        string url,
        string? expectedVersion,
        Func<JsonElement, IEnumerable<string?>> readVersions,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        using var response = _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
            return MissingPackage(url);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(ReadBoundedContent(response, maxResponseBytes, cancellationToken));
        var versions = readVersions(document.RootElement)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Select(static version => version!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (versions.Length == 0)
            return InvalidRegistryResponse("Registry response did not contain a non-empty package version collection.");
        if (!HasExactRegistryVersion(ecosystem, expectedVersion))
            return Verified();

        return versions.Any(version => VersionsEqual(version, expectedVersion))
            ? Verified()
            : MissingVersion(expectedVersion!);
    }

    private PackageVerificationOutcome VerifyRubyGems(
        WebAgentPackageReference package,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        var hasExactVersion = HasExactRegistryVersion("rubygems", package.Version);
        var url = !hasExactVersion
            ? $"https://rubygems.org/api/v1/gems/{Uri.EscapeDataString(package.Id)}.json"
            : $"https://rubygems.org/api/v2/rubygems/{Uri.EscapeDataString(package.Id)}/versions/{Uri.EscapeDataString(package.Version!)}.json";
        using var response = _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
            return MissingPackage(url);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(ReadBoundedContent(response, maxResponseBytes, cancellationToken));
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(versionElement.GetString()))
        {
            return InvalidRegistryResponse("RubyGems response did not contain package version metadata.");
        }
        return !hasExactVersion || VersionsEqual(versionElement.GetString(), package.Version)
            ? Verified()
            : MissingVersion(package.Version!);
    }

    private PackageVerificationOutcome VerifyPackagist(
        WebAgentPackageReference package,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        var url = $"https://repo.packagist.org/p2/{package.Id}.json";
        using var response = _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
            return MissingPackage(url);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(ReadBoundedContent(response, maxResponseBytes, cancellationToken));
        if (!document.RootElement.TryGetProperty("packages", out var packages) ||
            packages.ValueKind != JsonValueKind.Object ||
            !packages.TryGetProperty(package.Id, out var versions) ||
            versions.ValueKind != JsonValueKind.Array ||
            versions.GetArrayLength() == 0)
            return InvalidRegistryResponse("Packagist response did not contain a non-empty package version collection.");
        if (!HasExactRegistryVersion("packagist", package.Version))
            return Verified();

        return versions.EnumerateArray().Any(version =>
                   version.ValueKind == JsonValueKind.Object &&
                   version.TryGetProperty("version", out var value) &&
                   value.ValueKind == JsonValueKind.String &&
                   VersionsEqual(value.GetString(), package.Version))
            ? Verified()
            : MissingVersion(package.Version!);
    }

    private PackageVerificationOutcome VerifyPowerShellGallery(
        WebAgentPackageReference package,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        var escapedId = package.Id.Replace("'", "''", StringComparison.Ordinal);
        var url = $"https://www.powershellgallery.com/api/v2/FindPackagesById()?id='{Uri.EscapeDataString(escapedId)}'";
        using var response = _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
            return MissingPackage(url);
        response.EnsureSuccessStatusCode();
        using var stream = new MemoryStream(ReadBoundedContent(response, maxResponseBytes, cancellationToken), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader, LoadOptions.None);
        var atom = XNamespace.Get("http://www.w3.org/2005/Atom");
        var data = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices");
        if (document.Root?.Name != atom + "feed")
            return InvalidRegistryResponse("PowerShell Gallery response was not an Atom feed.");
        var versions = document.Root.Elements(atom + "entry")
            .SelectMany(entry => entry.Descendants(data + "Version"))
            .Select(static element => element.Value.Trim())
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .ToArray();
        if (!HasExactRegistryVersion("powershellgallery", package.Version))
            return versions.Length > 0
                ? Verified()
                : InvalidRegistryResponse("PowerShell Gallery response did not contain non-empty package version metadata.");
        return versions.Any(version => VersionsEqual(version, package.Version))
            ? Verified()
            : MissingVersion(package.Version!);
    }

    private static PackageVerificationOutcome Verified()
        => new(true, string.Empty, string.Empty);

    private static PackageVerificationOutcome MissingPackage(string registryUrl)
        => new(false, "PFAGENT.PACKAGE.NOT_FOUND",
            $"Package is not registered at its public registry ({registryUrl}).");

    private static PackageVerificationOutcome MissingVersion(string expectedVersion)
        => new(false, "PFAGENT.PACKAGE.VERSION_NOT_FOUND",
            $"Exact package version '{expectedVersion}' is not registered.");

    private static PackageVerificationOutcome InvalidRegistryResponse(string message)
        => new(false, "PFAGENT.PACKAGE.REGISTRY_INVALID_RESPONSE", message);

    private static byte[] ReadBoundedContent(
        HttpResponseMessage response,
        long maxResponseBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > maxResponseBytes)
            throw new RegistryResponseTooLargeException(maxResponseBytes);

        using var input = response.Content.ReadAsStream(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = input.ReadAsync(buffer.AsMemory(), cancellationToken).AsTask().GetAwaiter().GetResult();
            if (read == 0)
                break;
            if (output.Length + read > maxResponseBytes)
                throw new RegistryResponseTooLargeException(maxResponseBytes);
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static bool VersionsEqual(string? left, string? right)
        => string.Equals(
            left?.Trim().TrimStart('v'),
            right?.Trim().TrimStart('v'),
            StringComparison.OrdinalIgnoreCase);

    private static bool HasExactRegistryVersion(string ecosystem, string? version)
    {
        if (!WebPublicationCatalog.HasExactVersion(version))
            return false;
        if (ecosystem is "nuget" or "powershellgallery")
        {
            return Regex.IsMatch(
                version!,
                @"^v?\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
                RegexOptions.CultureInvariant);
        }
        if (ecosystem is "npm" or "crates")
        {
            return Regex.IsMatch(
                version!,
                @"^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
                RegexOptions.CultureInvariant);
        }
        return true;
    }

    private static void AddPackageFinding(
        ICollection<WebAgentContentSecurityFinding> findings,
        WebAgentPackageReference package,
        string code,
        string message)
        => AddFinding(findings, "error", code, package.Path, package.Line,
            $"{message} Command: {package.Command}; ecosystem: {package.Ecosystem}; package: {package.Id}.");

    private static WebPublicationCatalog? LoadOwnerCatalog(
        WebAgentContentSecurityOptions options,
        List<WebAgentContentSecurityFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(options.PublicationCatalogPath))
            return null;
        try
        {
            return WebPublicationCatalog.Load(
                options.PublicationCatalogPath,
                options.PublicationCatalogMaxAgeHours,
                "agent-content");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or
                                      UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            AddFinding(findings, "error", "PFAGENT.PACKAGE.INVALID_OWNER_CATALOG", null, null, ex.Message);
            return null;
        }
    }

    private static bool MatchesAnySelector(string value, IEnumerable<string>? selectors)
        => (selectors ?? Array.Empty<string>()).Any(selector => WildcardMatches(value, selector));

    private static bool IsValidPackageId(string ecosystem, string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 256 || id.Any(static character => character > 0x7F))
            return false;

        static bool IsSegment(string value)
            => value.Length > 0 && value.All(static character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

        return ecosystem switch
        {
            "nuget" or "powershellgallery" or "pypi" or "crates" or "rubygems" => IsSegment(id),
            "npm" when id.StartsWith('@') =>
                id.Count(static character => character == '/') == 1 &&
                id.Split('/').All(segment => IsSegment(segment.TrimStart('@'))),
            "npm" => IsSegment(id),
            "packagist" => id.Count(static character => character == '/') == 1 && id.Split('/').All(IsSegment),
            _ => false
        };
    }

    private static bool WildcardMatches(string value, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;
        var normalized = pattern.Trim();
        var valueIndex = 0;
        var patternIndex = 0;
        var starIndex = -1;
        var retryIndex = 0;
        while (valueIndex < value.Length)
        {
            if (patternIndex < normalized.Length &&
                (normalized[patternIndex] == '?' ||
                 char.ToUpperInvariant(normalized[patternIndex]) == char.ToUpperInvariant(value[valueIndex])))
            {
                valueIndex++;
                patternIndex++;
            }
            else if (patternIndex < normalized.Length && normalized[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                retryIndex = valueIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                valueIndex = ++retryIndex;
            }
            else
            {
                return false;
            }
        }
        while (patternIndex < normalized.Length && normalized[patternIndex] == '*')
            patternIndex++;
        return patternIndex == normalized.Length;
    }

    private sealed record PackageVerificationOutcome(bool Success, string Code, string Message);

    private sealed class RegistryResponseTooLargeException(long maxBytes)
        : Exception($"Registry response exceeds the configured {maxBytes}-byte decompressed limit.");

}
