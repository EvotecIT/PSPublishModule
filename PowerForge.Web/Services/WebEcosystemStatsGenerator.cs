using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates ecosystem statistics from GitHub, NuGet, and PowerShell Gallery.</summary>
public static class WebEcosystemStatsGenerator
{
    private const string GitHubApiBase = "https://api.github.com";
    private const string NuGetSearchApiBase = "https://api-v2v3search-0.nuget.org/query";
    private const string PowerShellGalleryApiBase = "https://www.powershellgallery.com/api/v2/Packages";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Generates ecosystem statistics JSON output.</summary>
    /// <param name="options">Generation options.</param>
    /// <param name="httpHandler">Optional HTTP handler override for testing.</param>
    /// <returns>Result payload.</returns>
    public static WebEcosystemStatsResult Generate(WebEcosystemStatsOptions options, HttpMessageHandler? httpHandler = null)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var hasGitHub = !string.IsNullOrWhiteSpace(options.GitHubOrganization);
        var hasNuGet = !string.IsNullOrWhiteSpace(options.NuGetOwner);
        var hasPowerShellGallery = !string.IsNullOrWhiteSpace(options.PowerShellGalleryOwner) ||
                                   !string.IsNullOrWhiteSpace(options.PowerShellGalleryAuthor);
        if (!hasGitHub && !hasNuGet && !hasPowerShellGallery)
            throw new InvalidOperationException("ecosystem-stats requires at least one source (GitHub/NuGet/PowerShell Gallery).");

        var warnings = new List<string>();
        var baseDir = ResolveBaseDirectory(options.BaseDirectory);
        var outputPath = ResolvePath(options.OutputPath, baseDir, warnings, "OutputPath");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("OutputPath is invalid.", nameof(options));

        var requestTimeoutSeconds = options.RequestTimeoutSeconds > 0 ? options.RequestTimeoutSeconds : 30;
        var maxItems = options.MaxItems > 0 ? options.MaxItems : 500;

        using var http = httpHandler is null
            ? CreateHttpClient(requestTimeoutSeconds)
            : new HttpClient(httpHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(requestTimeoutSeconds) };

        WebEcosystemGitHubStats? github = null;
        if (hasGitHub)
            github = FetchGitHubStats(http, options, maxItems, warnings);

        WebEcosystemNuGetStats? nuget = null;
        if (hasNuGet)
            nuget = FetchNuGetStats(http, options, maxItems, warnings);

        WebEcosystemPowerShellGalleryStats? powerShellGallery = null;
        if (hasPowerShellGallery)
            powerShellGallery = FetchPowerShellGalleryStats(http, options, maxItems, warnings);

        var summary = new WebEcosystemStatsSummary
        {
            RepositoryCount = github?.RepositoryCount ?? 0,
            NuGetPackageCount = nuget?.PackageCount ?? 0,
            PowerShellGalleryModuleCount = powerShellGallery?.ModuleCount ?? 0,
            GitHubStars = github?.TotalStars ?? 0,
            GitHubForks = github?.TotalForks ?? 0,
            NuGetDownloads = nuget?.TotalDownloads ?? 0,
            PowerShellGalleryDownloads = powerShellGallery?.TotalDownloads ?? 0
        };
        summary.TotalDownloads = summary.NuGetDownloads + summary.PowerShellGalleryDownloads;

        var document = new WebEcosystemStatsDocument
        {
            Title = string.IsNullOrWhiteSpace(options.Title) ? "Ecosystem Stats" : options.Title.Trim(),
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Summary = summary,
            GitHub = github,
            NuGet = nuget,
            PowerShellGallery = powerShellGallery,
            Warnings = warnings
        };

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDir))
            Directory.CreateDirectory(outputDir);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(document, WebJson.Options), Encoding.UTF8);

        return new WebEcosystemStatsResult
        {
            OutputPath = outputPath,
            RepositoryCount = summary.RepositoryCount,
            NuGetPackageCount = summary.NuGetPackageCount,
            PowerShellGalleryModuleCount = summary.PowerShellGalleryModuleCount,
            Warnings = warnings.ToArray()
        };
    }

    private static WebEcosystemGitHubStats FetchGitHubStats(
        HttpClient http,
        WebEcosystemStatsOptions options,
        int maxItems,
        List<string> warnings)
    {
        var organization = options.GitHubOrganization!.Trim();
        var repositories = new List<WebEcosystemGitHubRepository>();
        var page = 1;
        var pageSize = 100;

        while (repositories.Count < maxItems)
        {
            var take = Math.Min(pageSize, maxItems - repositories.Count);
            var url = $"{GitHubApiBase}/orgs/{Uri.EscapeDataString(organization)}/repos?per_page={take}&page={page}&type=public";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            if (!string.IsNullOrWhiteSpace(options.GitHubToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHubToken);

            using var response = Send(http, request, warnings, "GitHub");
            if (response is null)
                break;

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("GitHub response payload was not an array.");
                break;
            }

            var fetched = 0;
            foreach (var repoElement in document.RootElement.EnumerateArray())
            {
                var repository = ParseGitHubRepository(repoElement);
                if (repository is null)
                    continue;
                repositories.Add(repository);
                fetched++;
                if (repositories.Count >= maxItems)
                    break;
            }

            if (fetched < take)
                break;

            page++;
            if (page > 200)
            {
                warnings.Add("GitHub pagination limit reached (200 pages).");
                break;
            }
        }

        repositories = repositories
            .OrderByDescending(repository => repository.Stars)
            .ThenBy(repository => repository.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WebEcosystemGitHubStats
        {
            Organization = organization,
            RepositoryCount = repositories.Count,
            TotalStars = repositories.Sum(static repository => (long)repository.Stars),
            TotalForks = repositories.Sum(static repository => (long)repository.Forks),
            TotalWatchers = repositories.Sum(static repository => (long)repository.Watchers),
            TotalOpenIssues = repositories.Sum(static repository => (long)repository.OpenIssues),
            Repositories = repositories
        };
    }

    private static WebEcosystemGitHubRepository? ParseGitHubRepository(JsonElement element)
    {
        var name = ReadString(element, "name");
        var fullName = ReadString(element, "full_name");
        var htmlUrl = ReadString(element, "html_url");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(htmlUrl))
            return null;

        return new WebEcosystemGitHubRepository
        {
            Name = name,
            FullName = fullName,
            Url = htmlUrl,
            Language = ReadString(element, "language"),
            Archived = ReadBool(element, "archived"),
            Stars = ReadInt32(element, "stargazers_count"),
            Forks = ReadInt32(element, "forks_count"),
            Watchers = ReadInt32(element, "watchers_count"),
            OpenIssues = ReadInt32(element, "open_issues_count"),
            PushedAt = ReadDateTimeOffset(element, "pushed_at")
        };
    }

    private static WebEcosystemNuGetStats FetchNuGetStats(
        HttpClient http,
        WebEcosystemStatsOptions options,
        int maxItems,
        List<string> warnings)
    {
        var owner = options.NuGetOwner!.Trim();
        var packages = new Dictionary<string, WebEcosystemNuGetPackage>(StringComparer.OrdinalIgnoreCase);
        var skip = 0;
        var pageSize = 100;
        int? totalHits = null;

        while (packages.Count < maxItems)
        {
            var take = Math.Min(pageSize, maxItems - packages.Count);
            var query = Uri.EscapeDataString($"owner:{owner}");
            var url = $"{NuGetSearchApiBase}?q={query}&prerelease=true&take={take}&skip={skip}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = Send(http, request, warnings, "NuGet");
            if (response is null)
                break;

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("NuGet response payload was not an object.");
                break;
            }

            if (document.RootElement.TryGetProperty("totalHits", out var totalHitsElement) &&
                totalHitsElement.ValueKind is JsonValueKind.Number &&
                totalHitsElement.TryGetInt32(out var parsedTotalHits))
                totalHits = parsedTotalHits;

            if (!document.RootElement.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
            {
                warnings.Add("NuGet response payload did not include array 'data'.");
                break;
            }

            var fetched = 0;
            foreach (var packageElement in dataElement.EnumerateArray())
            {
                var package = ParseNuGetPackage(packageElement);
                if (package is null)
                    continue;

                packages[package.Id] = package;
                fetched++;
                if (packages.Count >= maxItems)
                    break;
            }

            if (fetched == 0)
                break;

            skip += fetched;
            if (totalHits.HasValue && skip >= totalHits.Value)
                break;
        }

        var items = packages.Values
            .OrderByDescending(package => package.TotalDownloads)
            .ThenBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WebEcosystemNuGetStats
        {
            Owner = owner,
            PackageCount = items.Count,
            TotalDownloads = items.Sum(static package => package.TotalDownloads),
            Items = items
        };
    }

    private static WebEcosystemNuGetPackage? ParseNuGetPackage(JsonElement element)
    {
        var id = ReadString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return new WebEcosystemNuGetPackage
        {
            Id = id,
            Version = ReadString(element, "version"),
            TotalDownloads = ReadInt64(element, "totalDownloads"),
            PackageUrl = ReadString(element, "packageUrl"),
            ProjectUrl = ReadString(element, "projectUrl"),
            Description = ReadString(element, "description"),
            Verified = ReadBool(element, "verified")
        };
    }

    private static WebEcosystemPowerShellGalleryStats FetchPowerShellGalleryStats(
        HttpClient http,
        WebEcosystemStatsOptions options,
        int maxItems,
        List<string> warnings)
    {
        var owner = options.PowerShellGalleryOwner?.Trim();
        var authorFilter = options.PowerShellGalleryAuthor?.Trim();
        var modules = new Dictionary<string, WebEcosystemPowerShellGalleryModule>(StringComparer.OrdinalIgnoreCase);

        var authorCandidates = BuildPowerShellGalleryAuthorCandidates(owner, authorFilter);
        foreach (var candidate in authorCandidates)
        {
            var filter = $"IsLatestVersion and Authors eq '{EscapeODataLiteral(candidate)}'";
            PullPowerShellGalleryModules(http, filter, maxItems, warnings, modules, requiredOwner: owner);
            if (modules.Count >= maxItems)
                break;
        }

        if (modules.Count == 0 && !string.IsNullOrWhiteSpace(owner))
        {
            var token = DerivePowerShellGallerySearchToken(owner);
            if (!string.IsNullOrWhiteSpace(token))
            {
                var filter = $"IsLatestVersion and substringof('{EscapeODataLiteral(token)}', Authors)";
                PullPowerShellGalleryModules(http, filter, maxItems, warnings, modules, requiredOwner: owner);
            }
        }

        var moduleItems = modules.Values
            .OrderByDescending(module => module.DownloadCount)
            .ThenBy(module => module.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WebEcosystemPowerShellGalleryStats
        {
            Owner = owner ?? authorFilter ?? string.Empty,
            AuthorFilter = authorFilter,
            ModuleCount = moduleItems.Count,
            TotalDownloads = moduleItems.Sum(static module => module.DownloadCount),
            Modules = moduleItems
        };
    }

    private static void PullPowerShellGalleryModules(
        HttpClient http,
        string filterExpression,
        int maxItems,
        List<string> warnings,
        Dictionary<string, WebEcosystemPowerShellGalleryModule> modules,
        string? requiredOwner)
    {
        var skip = 0;
        var pageSize = 100;

        while (modules.Count < maxItems)
        {
            var take = Math.Min(pageSize, maxItems - modules.Count);
            var encodedFilter = Uri.EscapeDataString(filterExpression);
            var url = $"{PowerShellGalleryApiBase}?$filter={encodedFilter}&$orderby=DownloadCount%20desc&$top={take}&$skip={skip}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = Send(http, request, warnings, "PowerShell Gallery");
            if (response is null)
                break;

            using var stream = response.Content.ReadAsStream();
            var document = LoadXmlSafe(stream, warnings);
            if (document is null)
                break;

            var entries = ParsePowerShellGalleryModules(document, requiredOwner);
            if (entries.Count == 0)
                break;

            foreach (var module in entries)
            {
                if (modules.TryGetValue(module.Id, out var existing))
                {
                    if (module.DownloadCount > existing.DownloadCount)
                        modules[module.Id] = module;
                    continue;
                }

                modules[module.Id] = module;
                if (modules.Count >= maxItems)
                    break;
            }

            skip += take;
            if (entries.Count < take)
                break;
        }
    }

    private static XDocument? LoadXmlSafe(Stream stream, List<string> warnings)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true
            };
            using var reader = XmlReader.Create(stream, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse PowerShell Gallery XML payload: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static List<WebEcosystemPowerShellGalleryModule> ParsePowerShellGalleryModules(XDocument document, string? requiredOwner)
    {
        var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");
        var dataNs = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices");
        var metadataNs = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");
        var modules = new List<WebEcosystemPowerShellGalleryModule>();

        foreach (var entry in document.Root?.Elements(atomNs + "entry") ?? Enumerable.Empty<XElement>())
        {
            var properties = entry.Element(atomNs + "content")?.Element(metadataNs + "properties");
            if (properties is null)
                continue;

            var id = properties.Element(dataNs + "Id")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var authors = properties.Element(dataNs + "Authors")?.Value?.Trim();
            var owners = properties.Element(dataNs + "Owners")?.Value?.Trim();
            if (!MatchesPowerShellGalleryOwner(requiredOwner, owners, authors))
                continue;

            var version = properties.Element(dataNs + "Version")?.Value?.Trim();
            var downloadText = properties.Element(dataNs + "DownloadCount")?.Value?.Trim();
            _ = long.TryParse(downloadText, out var downloadCount);

            modules.Add(new WebEcosystemPowerShellGalleryModule
            {
                Id = id,
                Version = version,
                DownloadCount = downloadCount,
                Authors = string.IsNullOrWhiteSpace(authors) ? null : authors,
                Owners = string.IsNullOrWhiteSpace(owners) ? null : owners,
                GalleryUrl = properties.Element(dataNs + "GalleryDetailsUrl")?.Value?.Trim(),
                ProjectUrl = properties.Element(dataNs + "ProjectUrl")?.Value?.Trim(),
                Description = properties.Element(dataNs + "Description")?.Value?.Trim()
            });
        }

        return modules;
    }

    private static bool MatchesPowerShellGalleryOwner(string? requiredOwner, string? owners, string? authors)
    {
        if (string.IsNullOrWhiteSpace(requiredOwner))
            return true;

        var required = NormalizeIdentity(requiredOwner);
        if (string.IsNullOrWhiteSpace(required))
            return true;

        var candidates = SplitOwnerOrAuthorValues(owners)
            .Concat(SplitOwnerOrAuthorValues(authors));
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeIdentity(candidate);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;
            if (string.Equals(normalized, required, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static List<string> BuildPowerShellGalleryAuthorCandidates(string? owner, string? author)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(author))
            values.Add(author.Trim());
        if (!string.IsNullOrWhiteSpace(owner))
        {
            values.Add(owner.Trim());
            values.Add(NormalizeOwnerToAuthor(owner));
        }

        return values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeOwnerToAuthor(string owner)
    {
        var normalized = owner.Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"[\.\-_]+",
            " ",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            RegexTimeout);
        normalized = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"\s+",
            " ",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            RegexTimeout);
        return normalized.Trim();
    }

    private static string DerivePowerShellGallerySearchToken(string owner)
    {
        var normalized = NormalizeOwnerToAuthor(owner);
        if (string.IsNullOrWhiteSpace(normalized))
            return owner.Trim();

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return normalized;
        return parts[0].Length >= 3 ? parts[0] : normalized;
    }

    private static IEnumerable<string> SplitOwnerOrAuthorValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var parts = value.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                yield return trimmed;
        }
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    private static string EscapeODataLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static HttpResponseMessage? Send(HttpClient http, HttpRequestMessage request, List<string> warnings, string sourceName)
    {
        try
        {
            var response = http.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                warnings.Add($"{sourceName} request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {request.RequestUri}");
                response.Dispose();
                return null;
            }

            return response;
        }
        catch (Exception ex)
        {
            warnings.Add($"{sourceName} request failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateHttpClient(int timeoutSeconds)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 5, 300))
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PowerForge.Web", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt32(out var intValue) && intValue != 0,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var boolValue) && boolValue,
            _ => false
        };
    }

    private static int ReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            return longValue > int.MaxValue ? int.MaxValue : (int)longValue;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;
        return 0;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            return longValue;
        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;
        return 0;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string propertyName)
    {
        var text = ReadString(element, propertyName);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return DateTimeOffset.TryParse(text, out var parsed)
            ? parsed
            : null;
    }

    private static string? ResolveBaseDirectory(string? baseDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            return null;
        try
        {
            return Path.GetFullPath(baseDir.Trim().Trim('"'));
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePath(string path, string? baseDir, List<string> warnings, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var trimmed = path.Trim().Trim('"');
        try
        {
            return Path.IsPathRooted(trimmed) || string.IsNullOrWhiteSpace(baseDir)
                ? Path.GetFullPath(trimmed)
                : Path.GetFullPath(Path.Combine(baseDir, trimmed));
        }
        catch (Exception ex)
        {
            warnings.Add($"{label} could not be resolved: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
