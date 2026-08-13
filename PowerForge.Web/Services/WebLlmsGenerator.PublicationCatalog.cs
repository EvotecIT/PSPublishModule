using System.Globalization;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebLlmsGenerator
{
    private static int ApplyInstallCommandPolicy(
        WebLlmsOptions options,
        IReadOnlyList<PackageInfo> packages,
        string? packageId,
        string? version,
        bool projectIsPowerShellModule,
        ref string? legacyInstallCommand)
    {
        if (options.ContentKind != WebLlmsContentKind.Package)
            return 0;

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.None)
        {
            legacyInstallCommand = null;
            foreach (var package in packages)
                package.InstallCommand = null;
            return 0;
        }

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.Declared)
            return packages.Count == 0
                ? string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1
                : packages.Count(HasInstallCommand);

        var catalog = LoadPublicationCatalog(options);
        if (packages.Count == 0)
        {
            if (string.IsNullOrWhiteSpace(packageId) ||
                !catalog.Contains(packageId, version, projectIsPowerShellModule, options))
                legacyInstallCommand = null;
            return string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1;
        }

        foreach (var package in packages)
        {
            if (!catalog.Contains(package.Id, package.Version, package.IsPowerShellModule, options))
                package.InstallCommand = null;
        }

        return packages.Count(HasInstallCommand);
    }

    private static PublicationCatalog LoadPublicationCatalog(WebLlmsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.PublicationCatalogPath))
            throw new InvalidOperationException(
                "VerifiedCatalog install policy requires publicationCatalogPath.");

        var path = Path.GetFullPath(options.PublicationCatalogPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Configured LLMS publication catalog not found: {path}", path);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Configured LLMS publication catalog is invalid JSON: {path}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"Configured LLMS publication catalog must be a JSON object: {path}");

            ValidateCatalogAge(root, path, options.PublicationCatalogMaxAgeHours);
            return new PublicationCatalog(
                ReadCatalogSection(root, "nuget", "packages"),
                ReadCatalogSection(root, "powerShellGallery", "modules"));
        }
    }

    private static void ValidateCatalogAge(JsonElement root, string path, int maxAgeHours)
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
                $"Configured LLMS publication catalog does not contain a valid generatedAtUtc timestamp: {path}");
        }

        if (generatedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            generatedAt < DateTimeOffset.UtcNow.AddHours(-maxAgeHours))
        {
            throw new InvalidDataException(
                $"Configured LLMS publication catalog is outside the accepted {maxAgeHours}-hour age window: {path}");
        }
    }

    private static PublicationCatalogSection? ReadCatalogSection(
        JsonElement root,
        string sectionName,
        string itemsName)
    {
        if (!root.TryGetProperty(sectionName, out var section) || section.ValueKind is JsonValueKind.Null)
            return null;
        if (section.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"LLMS publication catalog section '{sectionName}' must be an object.");

        var owner = section.TryGetProperty("owner", out var ownerElement) && ownerElement.ValueKind == JsonValueKind.String
            ? ownerElement.GetString()?.Trim()
            : null;
        if (!section.TryGetProperty(itemsName, out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                $"LLMS publication catalog section '{sectionName}' must contain array '{itemsName}'.");

        var packages = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.String)
                continue;
            var id = idElement.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(id))
            {
                var version = item.TryGetProperty("version", out var versionElement) &&
                              versionElement.ValueKind == JsonValueKind.String
                    ? versionElement.GetString()?.Trim()
                    : null;
                packages[id] = string.IsNullOrWhiteSpace(version) ? null : version;
            }
        }

        return new PublicationCatalogSection(owner, packages);
    }

    private sealed record PublicationCatalog(
        PublicationCatalogSection? NuGet,
        PublicationCatalogSection? PowerShellGallery)
    {
        public bool Contains(
            string packageId,
            string? expectedVersion,
            bool isPowerShellModule,
            WebLlmsOptions options)
        {
            var section = isPowerShellModule ? PowerShellGallery : NuGet;
            var expectedOwner = isPowerShellModule ? options.PowerShellGalleryOwner : options.NuGetOwner;
            var sourceName = isPowerShellModule ? "PowerShell Gallery" : "NuGet";

            if (string.IsNullOrWhiteSpace(expectedOwner))
                throw new InvalidOperationException(
                    $"VerifiedCatalog install policy requires the expected {sourceName} owner.");
            if (section is null)
                throw new InvalidDataException(
                    $"LLMS publication catalog does not contain a {sourceName} section.");
            if (!string.Equals(section.Owner, expectedOwner.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"LLMS publication catalog {sourceName} owner '{section.Owner}' does not match expected owner '{expectedOwner.Trim()}'.");

            if (!section.Packages.TryGetValue(packageId, out var publishedVersion))
                return false;
            if (!RequiresExactPublicationVersion(expectedVersion))
                return true;

            return string.Equals(publishedVersion, expectedVersion!.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool RequiresExactPublicationVersion(string? version)
        => !string.IsNullOrWhiteSpace(version) &&
           !string.Equals(version, "unknown", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(version, "varies by package", StringComparison.OrdinalIgnoreCase);

    private sealed record PublicationCatalogSection(
        string? Owner,
        Dictionary<string, string?> Packages);
}
