using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
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
                warningCount = finalDocument?.Warnings?.Count ?? result?.Warnings?.Length ?? 0
            };
            File.WriteAllText(summaryPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }

        stepResult.Success = true;
        if (usedFallback)
        {
            stepResult.Message = string.Equals(fallbackReason, "existing-on-warning-empty", StringComparison.Ordinal)
                ? $"ecosystem-stats fallback: preserved existing '{outputPath}' because new data had warnings and empty totals."
                : string.Equals(fallbackReason, "existing-source-on-warning-empty", StringComparison.Ordinal)
                    ? stepResult.Message
                : $"ecosystem-stats fallback: reused existing '{outputPath}'.";
            return;
        }

        var warningSuffix = result is null || result.Warnings.Length == 0
            ? string.Empty
            : $"; warnings={result.Warnings.Length}";
        stepResult.Message = $"ecosystem-stats ok: repos={result?.RepositoryCount ?? 0}; nuget={result?.NuGetPackageCount ?? 0}; psgallery={result?.PowerShellGalleryModuleCount ?? 0}{warningSuffix}";
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
            generated.PowerShellGallery = existing.PowerShellGallery;
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
