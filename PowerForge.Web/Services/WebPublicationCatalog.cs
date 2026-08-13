using System.Globalization;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Owner-scoped package publication evidence shared by generation and final-artifact auditing.</summary>
internal sealed class WebPublicationCatalog
{
    private readonly CatalogSection? _nuGet;
    private readonly CatalogSection? _powerShellGallery;
    private readonly string[] _warnings;
    private readonly string _contextLabel;

    private WebPublicationCatalog(
        CatalogSection? nuGet,
        CatalogSection? powerShellGallery,
        string[] warnings,
        string contextLabel)
    {
        _nuGet = nuGet;
        _powerShellGallery = powerShellGallery;
        _warnings = warnings;
        _contextLabel = contextLabel;
    }

    public static WebPublicationCatalog Load(string configuredPath, int maxAgeHours, string contextLabel)
    {
        var path = Path.GetFullPath(configuredPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configured {contextLabel} publication catalog not found: {path}", path);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configured {contextLabel} publication catalog is invalid JSON: {path}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Configured {contextLabel} publication catalog must be a JSON object: {path}");
            ValidateAge(root, path, maxAgeHours, contextLabel);
            return new WebPublicationCatalog(
                ReadSection(root, "nuget", "packages", contextLabel),
                ReadSection(root, "powerShellGallery", "modules", contextLabel),
                ReadWarnings(root, contextLabel),
                contextLabel);
        }
    }

    public bool ContainsExactOwnedPackage(
        string ecosystem,
        string packageId,
        string? expectedVersion,
        string? expectedOwner)
    {
        if (!ecosystem.Equals("nuget", StringComparison.OrdinalIgnoreCase) &&
            !ecosystem.Equals("powershellgallery", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Owner-scoped publication catalogs do not support ecosystem '{ecosystem}'.");
        }
        var isPowerShellGallery = ecosystem.Equals("powershellgallery", StringComparison.OrdinalIgnoreCase);
        var section = isPowerShellGallery ? _powerShellGallery : _nuGet;
        var sourceName = isPowerShellGallery ? "PowerShell Gallery" : "NuGet";
        if (string.IsNullOrWhiteSpace(expectedOwner))
            throw new InvalidOperationException(
                $"VerifiedCatalog install policy requires the expected {sourceName} owner.");
        if (section is null)
            throw new InvalidDataException(
                $"{_contextLabel} publication catalog does not contain a {sourceName} section.");
        if (!string.Equals(section.Owner, expectedOwner.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"{_contextLabel} publication catalog {sourceName} owner '{section.Owner}' does not match expected owner '{expectedOwner.Trim()}'.");
        if (_warnings.Any(warning =>
                warning.Contains(sourceName, StringComparison.OrdinalIgnoreCase) &&
                warning.Contains("preserv", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"{_contextLabel} publication catalog contains preserved stale {sourceName} data and cannot verify installation commands.");
        }

        return section.Packages.TryGetValue(packageId, out var publishedVersion) &&
               HasExactVersion(expectedVersion) &&
               VersionsEqual(publishedVersion, expectedVersion);
    }

    public static bool HasExactVersion(string? version)
        => !string.IsNullOrWhiteSpace(version) &&
           version.Any(char.IsDigit) &&
           !string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "varies by package", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "next", StringComparison.OrdinalIgnoreCase) &&
           version.IndexOfAny(new[] { '*', '^', '~', '<', '>', '=', '[', ']', '(', ')', ',', '|' }) < 0;

    private static bool VersionsEqual(string? left, string? right)
        => string.Equals(left?.Trim().TrimStart('v'), right?.Trim().TrimStart('v'), StringComparison.OrdinalIgnoreCase);

    private static void ValidateAge(JsonElement root, string path, int maxAgeHours, string contextLabel)
    {
        if (maxAgeHours <= 0)
            return;
        if (!root.TryGetProperty("generatedAtUtc", out var generatedElement) ||
            generatedElement.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(
                generatedElement.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var generatedAt))
        {
            throw new InvalidDataException(
                $"Configured {contextLabel} publication catalog does not contain a valid generatedAtUtc timestamp: {path}");
        }
        if (generatedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            generatedAt < DateTimeOffset.UtcNow.AddHours(-maxAgeHours))
        {
            throw new InvalidDataException(
                $"Configured {contextLabel} publication catalog is outside the accepted {maxAgeHours}-hour age window: {path}");
        }
    }

    private static CatalogSection? ReadSection(
        JsonElement root,
        string sectionName,
        string itemsName,
        string contextLabel)
    {
        if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind is JsonValueKind.Null)
            return null;
        if (section.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{contextLabel} publication catalog section '{sectionName}' must be an object.");
        if (!section.TryGetProperty(itemsName, out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                $"{contextLabel} publication catalog section '{sectionName}' must contain array '{itemsName}'.");

        var owner = section.TryGetProperty("owner", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.String
            ? ownerElement.GetString()?.Trim()
            : null;
        var packages = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
                continue;
            var id = idElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            packages[id] = item.TryGetProperty("version", out var versionElement) &&
                           versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()?.Trim()
                : null;
        }
        return new CatalogSection(owner, packages);
    }

    private static string[] ReadWarnings(JsonElement root, string contextLabel)
    {
        if (!root.TryGetProperty("warnings", out var warnings) || warnings.ValueKind is JsonValueKind.Null)
            return Array.Empty<string>();
        if (warnings.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{contextLabel} publication catalog 'warnings' must be an array.");
        return warnings.EnumerateArray()
            .Where(static warning => warning.ValueKind == JsonValueKind.String)
            .Select(static warning => warning.GetString()?.Trim())
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Select(static warning => warning!)
            .ToArray();
    }

    private sealed record CatalogSection(string? Owner, IReadOnlyDictionary<string, string?> Packages);
}
