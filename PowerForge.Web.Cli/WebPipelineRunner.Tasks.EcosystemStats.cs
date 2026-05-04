using System;
using System.IO;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static readonly HttpClient PowerShellGalleryPackageClient = CreatePowerShellGalleryPackageClient();

    private static void ExecuteEcosystemStats(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outputPath = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputPath") ??
            GetString(step, "output-path") ??
            "./data/ecosystem/stats.json");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new InvalidOperationException("ecosystem-stats requires out/output path.");

        var strict = GetBool(step, "strict") ?? false;
        var preserveOnWarnings = GetBool(step, "preserveOnWarnings") ??
                                 GetBool(step, "preserve-on-warnings") ??
                                 true;
        var publishPath = ResolvePath(baseDir,
            GetString(step, "publishPath") ??
            GetString(step, "publish-path") ??
            "./static/data/ecosystem/stats.json");
        var syncProjectCatalogTelemetry =
            GetBool(step, "syncProjectCatalogTelemetry") ??
            GetBool(step, "sync-project-catalog-telemetry") ??
            false;
        var projectCatalogPath = ResolvePath(baseDir,
            GetString(step, "projectCatalogPath") ??
            GetString(step, "project-catalog-path") ??
            GetString(step, "catalog") ??
            GetString(step, "catalogPath") ??
            GetString(step, "catalog-path") ??
            "./data/projects/catalog.json");
        var projectCatalogPublishPath = ResolvePath(baseDir,
            GetString(step, "projectCatalogPublishPath") ??
            GetString(step, "project-catalog-publish-path") ??
            GetString(step, "catalogPublishPath") ??
            GetString(step, "catalog-publish-path") ??
            "./static/data/projects/catalog.json");
        var summaryPath = ResolvePath(baseDir,
            GetString(step, "summaryPath") ??
            GetString(step, "summary-path") ??
            "./Build/sync-ecosystem-stats-last-run.json");

        var githubOrganization = GetString(step, "githubOrg") ??
                                 GetString(step, "github-org") ??
                                 GetString(step, "githubOrganization") ??
                                 GetString(step, "github-organization");
        var githubToken = GetString(step, "githubToken") ??
                          GetString(step, "github-token") ??
                          GetString(step, "token");
        var githubTokenEnv = GetString(step, "githubTokenEnv") ??
                             GetString(step, "github-token-env") ??
                             GetString(step, "tokenEnv") ??
                             GetString(step, "token-env");
        if (string.IsNullOrWhiteSpace(githubToken) && !string.IsNullOrWhiteSpace(githubTokenEnv))
            githubToken = Environment.GetEnvironmentVariable(githubTokenEnv);

        var nugetOwner = GetString(step, "nugetOwner") ?? GetString(step, "nuget-owner");
        var powerShellGalleryOwner = GetString(step, "psgalleryOwner") ??
                                     GetString(step, "psgallery-owner") ??
                                     GetString(step, "powerShellGalleryOwner") ??
                                     GetString(step, "powershell-gallery-owner");
        var powerShellGalleryAuthor = GetString(step, "psgalleryAuthor") ??
                                      GetString(step, "psgallery-author") ??
                                      GetString(step, "powerShellGalleryAuthor") ??
                                      GetString(step, "powershell-gallery-author");
        var title = GetString(step, "title");
        var maxItems = GetInt(step, "maxItems") ?? GetInt(step, "max-items") ?? 500;
        var timeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 30;
        var refreshPowerShellGalleryByIdOnFallback =
            GetBool(step, "refreshPowerShellGalleryByIdOnFallback") ??
            GetBool(step, "refresh-powershell-gallery-by-id-on-fallback") ??
            false;
        var powerShellGalleryByIdRefreshTimeoutSeconds =
            GetInt(step, "powerShellGalleryByIdRefreshTimeoutSeconds") ??
            GetInt(step, "powershell-gallery-by-id-refresh-timeout-seconds") ??
            // By default the per-ID fallback shares the step timeout; sites can give it a separate budget.
            timeoutSeconds;

        if (string.IsNullOrWhiteSpace(githubOrganization) &&
            string.IsNullOrWhiteSpace(nugetOwner) &&
            string.IsNullOrWhiteSpace(powerShellGalleryOwner) &&
            string.IsNullOrWhiteSpace(powerShellGalleryAuthor))
        {
            throw new InvalidOperationException("ecosystem-stats requires at least one source: githubOrg, nugetOwner, psgalleryOwner, or psgalleryAuthor.");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var existingOutputContent = File.Exists(outputPath)
            ? File.ReadAllText(outputPath)
            : null;
        var existingSnapshot = TryReadEcosystemStatsSnapshot(existingOutputContent);

        WebEcosystemStatsResult? result = null;
        var usedFallback = false;
        string? fallbackReason = null;

        try
        {
            result = WebEcosystemStatsGenerator.Generate(new WebEcosystemStatsOptions
            {
                OutputPath = outputPath,
                BaseDirectory = baseDir,
                Title = title,
                GitHubOrganization = githubOrganization,
                GitHubToken = githubToken,
                NuGetOwner = nugetOwner,
                PowerShellGalleryOwner = powerShellGalleryOwner,
                PowerShellGalleryAuthor = powerShellGalleryAuthor,
                MaxItems = maxItems > 0 ? maxItems : 500,
                RequestTimeoutSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30
            });
        }
        catch
        {
            if (!strict && File.Exists(outputPath))
            {
                usedFallback = true;
                fallbackReason = "existing-on-error";
            }
            else
            {
                throw;
            }
        }

        if (!usedFallback &&
            !strict &&
            preserveOnWarnings &&
            !string.IsNullOrWhiteSpace(existingOutputContent) &&
            result is not null &&
            result.Warnings.Length > 0)
        {
            if (TryPreserveEcosystemSources(
                    existingOutputContent,
                    outputPath,
                    hasGitHub: !string.IsNullOrWhiteSpace(githubOrganization),
                    hasNuGet: !string.IsNullOrWhiteSpace(nugetOwner),
                    hasPowerShellGallery: !string.IsNullOrWhiteSpace(powerShellGalleryOwner) || !string.IsNullOrWhiteSpace(powerShellGalleryAuthor),
                    refreshPowerShellGalleryByIdOnFallback,
                    powerShellGalleryByIdRefreshTimeoutSeconds,
                    out var preservedSources))
            {
                usedFallback = true;
                fallbackReason = "existing-source-on-warning-empty";
                stepResult.Message = $"ecosystem-stats fallback: preserved existing source data for {string.Join(", ", preservedSources)} in '{outputPath}'.";
            }

            var generatedSnapshot = TryReadEcosystemStatsSnapshotFromFile(outputPath);
            if (!usedFallback && ShouldPreserveExistingStats(existingSnapshot, generatedSnapshot))
            {
                File.WriteAllText(outputPath, existingOutputContent);
                usedFallback = true;
                fallbackReason = "existing-on-warning-empty";
            }
        }

        if (!string.IsNullOrWhiteSpace(publishPath) && File.Exists(outputPath))
        {
            var publishDir = Path.GetDirectoryName(publishPath);
            if (!string.IsNullOrWhiteSpace(publishDir))
                Directory.CreateDirectory(publishDir);
            File.Copy(outputPath, publishPath, overwrite: true);
        }

        var projectCatalogTelemetryMerged = 0;
        string? projectCatalogTelemetryWarning = null;
        if (syncProjectCatalogTelemetry)
        {
            projectCatalogTelemetryMerged = SyncProjectCatalogTelemetryFromStats(
                outputPath,
                projectCatalogPath,
                projectCatalogPublishPath,
                out projectCatalogTelemetryWarning);
        }

        if (!string.IsNullOrWhiteSpace(summaryPath))
        {
            var summaryDir = Path.GetDirectoryName(summaryPath);
            if (!string.IsNullOrWhiteSpace(summaryDir))
                Directory.CreateDirectory(summaryDir);

            var finalDocument = File.Exists(outputPath)
                ? TryReadEcosystemStatsDocument(File.ReadAllText(outputPath))
                : null;
            var finalSummary = finalDocument?.Summary ?? (finalDocument is null ? null : BuildSummaryFromDocument(finalDocument));

            var summary = new
            {
                generatedOn = DateTimeOffset.UtcNow.ToString("O"),
                outputPath = Path.GetFullPath(outputPath),
                publishPath = string.IsNullOrWhiteSpace(publishPath) ? null : Path.GetFullPath(publishPath),
                status = usedFallback ? "fallback" : "updated",
                reason = fallbackReason,
                repositoryCount = finalSummary?.RepositoryCount ?? result?.RepositoryCount,
                nugetPackageCount = finalSummary?.NuGetPackageCount ?? result?.NuGetPackageCount,
                powerShellGalleryModuleCount = finalSummary?.PowerShellGalleryModuleCount ?? result?.PowerShellGalleryModuleCount,
                projectCatalogTelemetry = new
                {
                    enabled = syncProjectCatalogTelemetry,
                    catalogPath = syncProjectCatalogTelemetry && !string.IsNullOrWhiteSpace(projectCatalogPath) ? Path.GetFullPath(projectCatalogPath) : null,
                    publishPath = syncProjectCatalogTelemetry && !string.IsNullOrWhiteSpace(projectCatalogPublishPath) ? Path.GetFullPath(projectCatalogPublishPath) : null,
                    merged = projectCatalogTelemetryMerged,
                    warning = projectCatalogTelemetryWarning
                },
                warningCount = finalDocument?.Warnings?.Count ?? result?.Warnings?.Length ?? 0
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        var catalogSuffix = syncProjectCatalogTelemetry
            ? $"; projectCatalogTelemetry={projectCatalogTelemetryMerged}"
            : string.Empty;
        if (usedFallback)
        {
            stepResult.Message = string.Equals(fallbackReason, "existing-on-warning-empty", StringComparison.Ordinal)
                ? $"ecosystem-stats fallback: preserved existing '{outputPath}' because new data had warnings and empty totals{catalogSuffix}."
                : string.Equals(fallbackReason, "existing-source-on-warning-empty", StringComparison.Ordinal)
                    ? $"{stepResult.Message}{catalogSuffix}"
                : $"ecosystem-stats fallback: reused existing '{outputPath}'{catalogSuffix}.";
            return;
        }

        var warningSuffix = result is null || result.Warnings.Length == 0
            ? string.Empty
            : $"; warnings={result.Warnings.Length}";
        stepResult.Message = $"ecosystem-stats ok: repos={result?.RepositoryCount ?? 0}; nuget={result?.NuGetPackageCount ?? 0}; psgallery={result?.PowerShellGalleryModuleCount ?? 0}{catalogSuffix}{warningSuffix}";
    }

    private static int SyncProjectCatalogTelemetryFromStats(
        string? statsPath,
        string? projectCatalogPath,
        string? projectCatalogPublishPath,
        out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(statsPath) || !File.Exists(statsPath))
            return 0;
        if (string.IsNullOrWhiteSpace(projectCatalogPath) || !File.Exists(projectCatalogPath))
            return 0;

        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        ProjectCatalogDocument? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<ProjectCatalogDocument>(File.ReadAllText(projectCatalogPath), serializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            warning = $"Project catalog telemetry sync skipped: {ex.GetType().Name}: {ex.Message}";
            return 0;
        }

        if (catalog?.Projects is null || catalog.Projects.Count == 0)
            return 0;

        var merged = MergeProjectTelemetry(catalog.Projects, statsPath, serializerOptions);
        if (merged <= 0)
            return 0;

        catalog.GeneratedOn = DateTimeOffset.UtcNow.ToString("O");
        catalog.Projects = catalog.Projects
            .OrderBy(static value => value.Name ?? value.Slug ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var catalogDirectory = Path.GetDirectoryName(projectCatalogPath);
        if (!string.IsNullOrWhiteSpace(catalogDirectory))
            Directory.CreateDirectory(catalogDirectory);
        File.WriteAllText(projectCatalogPath, JsonSerializer.Serialize(catalog, serializerOptions));

        if (!string.IsNullOrWhiteSpace(projectCatalogPublishPath))
        {
            var publishDirectory = Path.GetDirectoryName(projectCatalogPublishPath);
            if (!string.IsNullOrWhiteSpace(publishDirectory))
                Directory.CreateDirectory(publishDirectory);
            File.Copy(projectCatalogPath, projectCatalogPublishPath, overwrite: true);
        }

        return merged;
    }

    private static EcosystemStatsSnapshot? TryReadEcosystemStatsSnapshotFromFile(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return TryReadEcosystemStatsSnapshot(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static EcosystemStatsSnapshot? TryReadEcosystemStatsSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
                return null;

            var repositoryCount = ReadInt32Property(summary, "repositoryCount");
            var nugetPackageCount = ReadInt32Property(summary, "nuGetPackageCount");
            var powerShellGalleryModuleCount = ReadInt32Property(summary, "powerShellGalleryModuleCount");
            var totalDownloads = ReadInt64Property(summary, "totalDownloads");

            return new EcosystemStatsSnapshot(repositoryCount, nugetPackageCount, powerShellGalleryModuleCount, totalDownloads);
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldPreserveExistingStats(EcosystemStatsSnapshot? existing, EcosystemStatsSnapshot? generated)
    {
        if (existing is null || generated is null)
            return false;

        return existing.HasData && generated.IsEmpty;
    }

    private static bool TryPreserveEcosystemSources(
        string existingJson,
        string generatedPath,
        bool hasGitHub,
        bool hasNuGet,
        bool hasPowerShellGallery,
        bool refreshPowerShellGalleryByIdOnFallback,
        int powerShellGalleryByIdRefreshTimeoutSeconds,
        out string[] preservedSources)
    {
        preservedSources = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(existingJson) || !File.Exists(generatedPath))
            return false;

        var existing = TryReadEcosystemStatsDocument(existingJson);
        var generated = TryReadEcosystemStatsDocument(File.ReadAllText(generatedPath));
        if (existing is null || generated is null)
            return false;

        var preserved = new List<string>();

        if (hasGitHub &&
            HasSourceWarning(generated.Warnings, "GitHub") &&
            HasGitHubData(existing) &&
            !HasGitHubData(generated))
        {
            generated.GitHub = existing.GitHub;
            preserved.Add("GitHub");
        }

        if (hasNuGet &&
            HasSourceWarning(generated.Warnings, "NuGet") &&
            HasNuGetData(existing) &&
            !HasNuGetData(generated))
        {
            generated.NuGet = existing.NuGet;
            preserved.Add("NuGet");
        }

        if (hasPowerShellGallery &&
            HasSourceWarning(generated.Warnings, "PowerShell Gallery") &&
            HasPowerShellGalleryData(existing) &&
            !HasPowerShellGalleryData(generated))
        {
            var refreshedCount = 0;
            var refreshed = refreshPowerShellGalleryByIdOnFallback
                ? TryRefreshPowerShellGalleryModulesById(
                    existing.PowerShellGallery,
                    generated.Warnings,
                    powerShellGalleryByIdRefreshTimeoutSeconds,
                    out refreshedCount)
                : null;
            generated.PowerShellGallery = refreshed ?? existing.PowerShellGallery;
            if (refreshed is not null)
                generated.Warnings.Add($"Refreshed {refreshedCount} preserved PowerShell Gallery modules by package ID after owner query failed.");
            preserved.Add("PowerShell Gallery");
        }

        if (preserved.Count == 0)
            return false;

        generated.Warnings ??= new List<string>();
        foreach (var source in preserved)
            generated.Warnings.Add($"Preserved existing {source} stats after upstream fetch warnings returned empty data.");

        generated.Warnings = generated.Warnings
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        generated.Summary = BuildSummaryFromDocument(generated);
        File.WriteAllText(generatedPath, JsonSerializer.Serialize(generated, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        preservedSources = preserved.ToArray();
        return true;
    }

    private static WebEcosystemPowerShellGalleryStats? TryRefreshPowerShellGalleryModulesById(
        WebEcosystemPowerShellGalleryStats? existing,
        List<string> warnings,
        int timeoutSeconds,
        out int refreshedCount)
    {
        refreshedCount = 0;
        if (existing?.Modules is null || existing.Modules.Count == 0)
            return null;

        var boundedTimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 300);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(boundedTimeoutSeconds));

        PowerShellGalleryModuleRefreshResult[] results;
        try
        {
            // The pipeline runner is synchronous; this bounded async batch is bridged once at the edge.
            results = RefreshPowerShellGalleryModulesByIdAsync(PowerShellGalleryPackageClient, existing.Modules, cancellation.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            warnings.Add($"PowerShell Gallery package refresh failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        var modules = new List<WebEcosystemPowerShellGalleryModule>(results.Length);
        foreach (var result in results)
        {
            var warning = result.Warning;
            if (!string.IsNullOrWhiteSpace(warning))
                warnings.Add(warning);

            if (result.Refreshed is null)
            {
                modules.Add(result.Existing);
                continue;
            }

            modules.Add(MergePowerShellGalleryModule(result.Existing, result.Refreshed));
            refreshedCount++;
        }

        if (refreshedCount == 0)
            return null;

        modules = modules
            .OrderByDescending(static module => module.DownloadCount)
            .ThenBy(static module => module.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WebEcosystemPowerShellGalleryStats
        {
            Owner = existing.Owner,
            AuthorFilter = existing.AuthorFilter,
            ModuleCount = modules.Count,
            TotalDownloads = modules.Sum(static module => module.DownloadCount),
            Modules = modules
        };
    }

    private static HttpClient CreatePowerShellGalleryPackageClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge.Web/1.0");
        return client;
    }

    private static async Task<PowerShellGalleryModuleRefreshResult[]> RefreshPowerShellGalleryModulesByIdAsync(
        HttpClient client,
        IReadOnlyList<WebEcosystemPowerShellGalleryModule> modules,
        CancellationToken cancellationToken)
    {
        const int maxConcurrency = 8;
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = modules.Select(async module =>
        {
            var id = module.Id;
            if (string.IsNullOrWhiteSpace(id))
                return new PowerShellGalleryModuleRefreshResult(module, null, null);

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var refreshed = await TryFetchPowerShellGalleryModuleByIdAsync(client, id, cancellationToken).ConfigureAwait(false);
                return new PowerShellGalleryModuleRefreshResult(module, refreshed, null);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or XmlException or IOException)
            {
                // The fallback is best-effort: modules that finish refresh, modules that time out keep existing metrics.
                return new PowerShellGalleryModuleRefreshResult(
                    module,
                    null,
                    $"PowerShell Gallery package refresh failed for {id}: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static WebEcosystemPowerShellGalleryModule MergePowerShellGalleryModule(
        WebEcosystemPowerShellGalleryModule existing,
        WebEcosystemPowerShellGalleryModule refreshed)
    {
        return new WebEcosystemPowerShellGalleryModule
        {
            Id = string.IsNullOrWhiteSpace(refreshed.Id) ? existing.Id : refreshed.Id,
            Version = string.IsNullOrWhiteSpace(refreshed.Version) ? existing.Version : refreshed.Version,
            DownloadCount = refreshed.DownloadCount > 0 ? refreshed.DownloadCount : existing.DownloadCount,
            Authors = string.IsNullOrWhiteSpace(refreshed.Authors) ? existing.Authors : refreshed.Authors,
            Owners = string.IsNullOrWhiteSpace(refreshed.Owners) ? existing.Owners : refreshed.Owners,
            GalleryUrl = string.IsNullOrWhiteSpace(refreshed.GalleryUrl) ? existing.GalleryUrl : refreshed.GalleryUrl,
            ProjectUrl = string.IsNullOrWhiteSpace(refreshed.ProjectUrl) ? existing.ProjectUrl : refreshed.ProjectUrl,
            Description = string.IsNullOrWhiteSpace(refreshed.Description) ? existing.Description : refreshed.Description
        };
    }

    private static async Task<WebEcosystemPowerShellGalleryModule?> TryFetchPowerShellGalleryModuleByIdAsync(
        HttpClient client,
        string id,
        CancellationToken cancellationToken)
    {
        var url = BuildPowerShellGalleryPackageByIdUrl(id);

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true
        };
        using var reader = XmlReader.Create(stream, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        return ParseFirstPowerShellGalleryModule(document);
    }

    private static string BuildPowerShellGalleryPackageByIdUrl(string id)
    {
        var odataLiteral = id.Replace("'", "''", StringComparison.Ordinal);
        var filter = $"Id eq '{odataLiteral}' and IsLatestVersion eq true";
        return "https://www.powershellgallery.com/api/v2/Packages?$filter=" + Uri.EscapeDataString(filter);
    }

    private static WebEcosystemPowerShellGalleryModule? ParseFirstPowerShellGalleryModule(XDocument document)
    {
        var atomNs = XNamespace.Get("http://www.w3.org/2005/Atom");
        var dataNs = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices");
        var metadataNs = XNamespace.Get("http://schemas.microsoft.com/ado/2007/08/dataservices/metadata");

        var entry = document.Root?.Elements(atomNs + "entry").FirstOrDefault();
        if (entry is null)
            return null;

        var properties = entry.Element(metadataNs + "properties") ??
                         entry.Element(atomNs + "content")?.Element(metadataNs + "properties");
        if (properties is null)
            return null;

        var id = properties.Element(dataNs + "Id")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        _ = long.TryParse(properties.Element(dataNs + "DownloadCount")?.Value?.Trim(), out var downloadCount);
        return new WebEcosystemPowerShellGalleryModule
        {
            Id = id,
            Version = properties.Element(dataNs + "Version")?.Value?.Trim(),
            DownloadCount = downloadCount,
            Authors = properties.Element(dataNs + "Authors")?.Value?.Trim(),
            Owners = properties.Element(dataNs + "Owners")?.Value?.Trim(),
            GalleryUrl = properties.Element(dataNs + "GalleryDetailsUrl")?.Value?.Trim(),
            ProjectUrl = properties.Element(dataNs + "ProjectUrl")?.Value?.Trim(),
            Description = properties.Element(dataNs + "Description")?.Value?.Trim()
        };
    }

    private sealed record PowerShellGalleryModuleRefreshResult(
        WebEcosystemPowerShellGalleryModule Existing,
        WebEcosystemPowerShellGalleryModule? Refreshed,
        string? Warning);

    private static WebEcosystemStatsDocument? TryReadEcosystemStatsDocument(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<WebEcosystemStatsDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static bool HasSourceWarning(IEnumerable<string>? warnings, string sourceName)
    {
        if (warnings is null)
            return false;

        return warnings.Any(warning => !string.IsNullOrWhiteSpace(warning) &&
                                       warning.Contains(sourceName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasGitHubData(WebEcosystemStatsDocument document)
    {
        return document.GitHub is { } gitHub &&
               (gitHub.RepositoryCount > 0 || gitHub.Repositories.Count > 0);
    }

    private static bool HasNuGetData(WebEcosystemStatsDocument document)
    {
        return document.NuGet is { } nuget &&
               (nuget.PackageCount > 0 || nuget.Items.Count > 0 || nuget.TotalDownloads > 0);
    }

    private static bool HasPowerShellGalleryData(WebEcosystemStatsDocument document)
    {
        return document.PowerShellGallery is { } gallery &&
               (gallery.ModuleCount > 0 || gallery.Modules.Count > 0 || gallery.TotalDownloads > 0);
    }

    private static WebEcosystemStatsSummary BuildSummaryFromDocument(WebEcosystemStatsDocument document)
    {
        var summary = new WebEcosystemStatsSummary
        {
            RepositoryCount = document.GitHub?.RepositoryCount ?? document.GitHub?.Repositories.Count ?? 0,
            NuGetPackageCount = document.NuGet?.PackageCount ?? document.NuGet?.Items.Count ?? 0,
            PowerShellGalleryModuleCount = document.PowerShellGallery?.ModuleCount ?? document.PowerShellGallery?.Modules.Count ?? 0,
            GitHubStars = document.GitHub?.TotalStars ?? 0,
            GitHubForks = document.GitHub?.TotalForks ?? 0,
            NuGetDownloads = document.NuGet?.TotalDownloads ?? 0,
            PowerShellGalleryDownloads = document.PowerShellGallery?.TotalDownloads ?? 0
        };
        summary.TotalDownloads = summary.NuGetDownloads + summary.PowerShellGalleryDownloads;
        return summary;
    }

    private static int ReadInt32Property(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var intValue))
            return intValue;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            return longValue > int.MaxValue ? int.MaxValue : (int)longValue;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return 0;
    }

    private static long ReadInt64Property(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            return longValue;

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var parsed))
            return parsed;

        return 0;
    }

    private sealed record EcosystemStatsSnapshot(
        int RepositoryCount,
        int NuGetPackageCount,
        int PowerShellGalleryModuleCount,
        long TotalDownloads)
    {
        public bool HasData =>
            RepositoryCount > 0 ||
            NuGetPackageCount > 0 ||
            PowerShellGalleryModuleCount > 0 ||
            TotalDownloads > 0;

        public bool IsEmpty =>
            RepositoryCount <= 0 &&
            NuGetPackageCount <= 0 &&
            PowerShellGalleryModuleCount <= 0 &&
            TotalDownloads <= 0;
    }
}
