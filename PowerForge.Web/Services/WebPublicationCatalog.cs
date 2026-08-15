using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowerForge.Web;

/// <summary>Owner-scoped package publication evidence shared by generation and final-artifact auditing.</summary>
internal sealed class WebPublicationCatalog
{
    /// <summary>Maximum accepted UTF-8 catalog payload size.</summary>
    internal const int MaximumCatalogBytes = 5 * 1024 * 1024;

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
            document = JsonDocument.Parse(ReadBoundedCatalog(path));
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
                ReadSection(root, "nuget", "packages", contextLabel, includeItemOwners: false),
                ReadSection(root, "powerShellGallery", "modules", contextLabel, includeItemOwners: true),
                ReadWarnings(root, contextLabel),
                contextLabel);
        }
    }

    private static ReadOnlyMemory<byte> ReadBoundedCatalog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
            FileOptions.SequentialScan);
        using var memory = new MemoryStream();
        Span<byte> buffer = stackalloc byte[8192];
        while (true)
        {
            var read = stream.Read(buffer);
            if (read == 0)
                break;
            if (memory.Length + read > MaximumCatalogBytes)
            {
                throw new InvalidDataException(
                    $"Configured publication catalog exceeds the {MaximumCatalogBytes}-byte safety limit: {path}");
            }
            memory.Write(buffer[..read]);
        }
        return memory.ToArray();
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

        return section.Packages.TryGetValue(packageId, out var package) &&
               HasExactOwnedRegistryVersion(ecosystem, expectedVersion) &&
               (isPowerShellGallery
                   ? VersionsEqual(package.Version, expectedVersion)
                   : WebPackageVersionIdentity.NuGetVersionsEqual(package.Version, expectedVersion)) &&
               (!isPowerShellGallery || PackageHasOwner(package.Owners, expectedOwner.Trim()));
    }

    public static bool HasExactVersion(string? version)
        => !string.IsNullOrWhiteSpace(version) &&
           version.Any(char.IsDigit) &&
           !string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "varies by package", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "latest", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "next", StringComparison.OrdinalIgnoreCase) &&
           version.IndexOfAny(new[] { '*', '^', '~', '<', '>', '=', '[', ']', '(', ')', ',', '|' }) < 0;

    private static bool HasExactOwnedRegistryVersion(string ecosystem, string? version)
        => ecosystem is "nuget" or "powershellgallery" &&
           HasExactVersion(version) &&
           Regex.IsMatch(
               version!,
               @"^v?\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$",
               RegexOptions.CultureInvariant);

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
        string contextLabel,
        bool includeItemOwners)
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
        var packages = new Dictionary<string, CatalogPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
                continue;
            var id = idElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var version = item.TryGetProperty("version", out var versionElement) &&
                          versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()?.Trim()
                : null;
            var owners = includeItemOwners && item.TryGetProperty("owners", out var ownersElement) &&
                         ownersElement.ValueKind == JsonValueKind.String
                ? ownersElement.GetString()?.Trim()
                : null;
            packages[id] = new CatalogPackage(version, owners);
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

    private static bool PackageHasOwner(string? owners, string expectedOwner)
        => WebPackageOwnerIdentity.Split(owners)
            .Any(owner => owner.Equals(expectedOwner, StringComparison.OrdinalIgnoreCase));

    private sealed record CatalogSection(string? Owner, IReadOnlyDictionary<string, CatalogPackage> Packages);
    private sealed record CatalogPackage(string? Version, string? Owners);
}
