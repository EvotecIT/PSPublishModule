using System.Globalization;
using System.Text.Json;
using PowerForgeStudio.Domain.Packages;

namespace PowerForgeStudio.Orchestrator.Packages;

/// <summary>Consumes the public PowerForge.Web ecosystem artifact without reading registry credentials.</summary>
public sealed class WorkspacePackageCatalogService(HttpClient? httpClient = null) : IWorkspacePackageCatalogService
{
    public const string SourceUrl = "https://evotec.xyz/data/ecosystem/stats.json";
    private const int MaxDocumentBytes = 2 * 1024 * 1024;
    private const int MaxPackages = 1_000;
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http = httpClient ?? SharedClient;

    public async Task<WorkspacePackageSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SourceUrl);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDocumentBytes)
            throw new InvalidDataException("The public package snapshot exceeds the Studio size limit.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + read > MaxDocumentBytes)
                throw new InvalidDataException("The public package snapshot exceeds the Studio size limit.");
            bytes.Write(buffer, 0, read);
        }
        bytes.Position = 0;
        using var document = await JsonDocument.ParseAsync(bytes, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken)
            .ConfigureAwait(false);
        return Parse(document.RootElement, DateTimeOffset.UtcNow);
    }

    private static WorkspacePackageSnapshot Parse(JsonElement root, DateTimeOffset readAtUtc)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("generatedAtUtc", out var generated) || generated.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(generated.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var generatedAtUtc) ||
            generatedAtUtc > readAtUtc.AddMinutes(5))
            throw new InvalidDataException("The public package snapshot has no valid generation time.");

        var packages = new List<WorkspacePackageMetric>();
        var hasNuGetSection = ReadItems(root, "nuget", "packages", "NuGet.org", "totalDownloads", "https://www.nuget.org/packages/", packages, out var rejectedNuGet);
        var hasGallerySection = ReadItems(root, "powerShellGallery", "modules", "PowerShell Gallery", "downloadCount",
            "https://www.powershellgallery.com/packages/", packages, out var rejectedGallery);
        if (packages.Count == 0) throw new InvalidDataException("The public package snapshot contains no package entries.");

        var summary = root.TryGetProperty("summary", out var summaryElement) && summaryElement.ValueKind == JsonValueKind.Object
            ? summaryElement : default;
        var warningSummaries = new List<string>();
        var sourceWarningCount = 0;
        var nuGetReadFailed = false;
        var galleryReadFailed = false;
        if (root.TryGetProperty("warnings", out var warnings) && warnings.ValueKind == JsonValueKind.Array)
        {
            sourceWarningCount = warnings.GetArrayLength();
            foreach (var warning in warnings.EnumerateArray())
            {
                if (warning.ValueKind != JsonValueKind.String) continue;
                var warningText = warning.GetString();
                if (IndicatesProviderFailure(warningText, "NuGet")) nuGetReadFailed = true;
                if (IndicatesProviderFailure(warningText, "PowerShell Gallery")) galleryReadFailed = true;
                var summaryLine = SummarizeWarning(warningText);
                if (summaryLine is not null && !warningSummaries.Contains(summaryLine, StringComparer.Ordinal))
                    warningSummaries.Add(summaryLine);
            }
        }
        var hasNuGet = hasNuGetSection && !(nuGetReadFailed && !packages.Any(static p => p.Registry == "NuGet.org"));
        var hasGallery = hasGallerySection && !(galleryReadFailed && !packages.Any(static p => p.Registry == "PowerShell Gallery"));
        if (!hasNuGet) warningSummaries.Add("NuGet.org unavailable; no count reported.");
        if (!hasGallery) warningSummaries.Add("PowerShell Gallery unavailable; no count reported.");
        if (rejectedNuGet + rejectedGallery > 0)
            warningSummaries.Add($"{rejectedNuGet + rejectedGallery} package rows skipped because IDs were invalid or duplicated.");
        var warningCount = sourceWarningCount + (hasNuGet ? 0 : 1) + (hasGallery ? 0 : 1) + rejectedNuGet + rejectedGallery;
        return new WorkspacePackageSnapshot(generatedAtUtc, readAtUtc, packages,
            hasNuGet ? ReadNonNegative(summary, "nuGetDownloads") : null,
            hasGallery ? ReadNonNegative(summary, "powerShellGalleryDownloads") : null,
            warningCount, SourceUrl, hasNuGet, hasGallery, warningSummaries.Take(5).ToArray());
    }

    private static bool ReadItems(JsonElement root, string sectionName, string listName, string registry,
        string downloadName, string detailsBase, ICollection<WorkspacePackageMetric> result, out int rejected)
    {
        rejected = 0;
        if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind != JsonValueKind.Object ||
            !section.TryGetProperty(listName, out var items) || items.ValueKind != JsonValueKind.Array)
            return false;
        if (items.GetArrayLength() > MaxPackages || result.Count + items.GetArrayLength() > MaxPackages)
            throw new InvalidDataException("The public package snapshot exceeds the Studio entry limit.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { rejected++; continue; }
            var id = ReadString(item, "id");
            if (id is null || id.Length > 180 || !id.All(static c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-') ||
                !seen.Add(id)) { rejected++; continue; }
            result.Add(new WorkspacePackageMetric(id, registry, ReadString(item, "version"),
                ReadNonNegative(item, downloadName), detailsBase + Uri.EscapeDataString(id)));
        }
        return true;
    }

    private static string? SummarizeWarning(string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning)) return null;
        var gallery = warning.Contains("PowerShell Gallery", StringComparison.OrdinalIgnoreCase);
        var nuget = warning.Contains("NuGet", StringComparison.OrdinalIgnoreCase);
        var prefix = gallery ? "PowerShell Gallery" : nuget ? "NuGet.org" : "Ecosystem snapshot";
        if (warning.Contains("Preserved existing", StringComparison.OrdinalIgnoreCase))
            return $"{prefix}: prior statistics retained; freshness may differ from snapshot time.";
        if (warning.Contains("preserved", StringComparison.OrdinalIgnoreCase) && warning.Contains("package ID", StringComparison.OrdinalIgnoreCase))
            return $"{prefix}: known packages refreshed individually after owner query failed.";
        if (warning.Contains("request failed", StringComparison.OrdinalIgnoreCase))
            return $"{prefix}: upstream request failed.";
        return $"{prefix}: source reported a warning.";
    }

    private static bool IndicatesProviderFailure(string? warning, string provider)
        => warning?.Contains(provider, StringComparison.OrdinalIgnoreCase) == true &&
           (warning.Contains("request failed", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("response payload", StringComparison.OrdinalIgnoreCase) ||
            warning.Contains("Failed to parse", StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() : null;

    private static long? ReadNonNegative(JsonElement item, string name)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;
}
