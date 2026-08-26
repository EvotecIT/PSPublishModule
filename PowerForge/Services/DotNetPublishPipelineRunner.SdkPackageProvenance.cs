using System.Text.Json;
using NuGet.Versioning;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void AddSdkManagedPackageHashes(
        JsonElement properties,
        string projectDirectory,
        IEnumerable<string> packageRoots,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        string assetsPath = ReadEvaluatedPath(properties, "ProjectAssetsFile", projectDirectory)
            ?? Path.Combine(projectDirectory, "obj", "project.assets.json");
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var autoReferenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var downloads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (document.RootElement.TryGetProperty("project", out JsonElement project) &&
                project.TryGetProperty("frameworks", out JsonElement frameworks) &&
                frameworks.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty framework in frameworks.EnumerateObject())
                {
                    if (framework.Value.TryGetProperty("dependencies", out JsonElement dependencies) &&
                        dependencies.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty dependency in dependencies.EnumerateObject())
                        {
                            if (dependency.Value.TryGetProperty("autoReferenced", out JsonElement value) &&
                                value.ValueKind == JsonValueKind.True)
                            {
                                autoReferenced.Add(dependency.Name);
                            }
                        }
                    }

                    AddSdkDownloadDependencies(framework.Value, downloads);
                }
            }

            if (autoReferenced.Count > 0 &&
                document.RootElement.TryGetProperty("libraries", out JsonElement libraries) &&
                libraries.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty library in libraries.EnumerateObject())
                    AddAutoReferencedPackageHash(
                        library,
                        autoReferenced,
                        hashes,
                        sdkManagedPackageKeys);
            }

            foreach (string download in downloads)
                AddSdkDownloadPackageHash(
                    download,
                    packageRoots,
                    hashes,
                    sdkManagedPackageKeys);
        }
        catch
        {
            // Only an exact SDK-managed package hash can extend the committed lock.
        }
    }

    private static void AddSdkDownloadDependencies(JsonElement framework, HashSet<string> downloads)
    {
        if (!framework.TryGetProperty("downloadDependencies", out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement download in values.EnumerateArray())
        {
            if (!download.TryGetProperty("name", out JsonElement name) ||
                name.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(name.GetString()) ||
                !download.TryGetProperty("version", out JsonElement version) ||
                version.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(version.GetString()) ||
                !VersionRange.TryParse(version.GetString()!, out VersionRange? range) ||
                range.MinVersion is null ||
                range.MaxVersion is null ||
                !range.IsMinInclusive ||
                !range.IsMaxInclusive ||
                !range.MinVersion.Equals(range.MaxVersion))
            {
                continue;
            }

            downloads.Add(name.GetString()! + "|" + range.MinVersion.ToNormalizedString());
        }
    }

    private static void AddAutoReferencedPackageHash(
        JsonProperty library,
        HashSet<string> autoReferenced,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        int separator = library.Name.LastIndexOf('/');
        if (separator <= 0 || separator == library.Name.Length - 1)
            return;

        string packageId = library.Name.Substring(0, separator);
        if (!autoReferenced.Contains(packageId) ||
            !library.Value.TryGetProperty("type", out JsonElement type) ||
            !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase) ||
            !library.Value.TryGetProperty("sha512", out JsonElement sha512) ||
            sha512.ValueKind != JsonValueKind.String)
        {
            return;
        }

        string packageKey = packageId + "|" + library.Name.Substring(separator + 1);
        AddPackageHash(packageKey, sha512.GetString(), hashes);
        AddSdkManagedPackageKey(packageKey, sha512.GetString(), hashes, sdkManagedPackageKeys);
    }

    private static void AddSdkDownloadPackageHash(
        string packageKey,
        IEnumerable<string> packageRoots,
        Dictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        string[] parts = packageKey.Split('|');
        if (parts.Length != 2)
            return;

        string? discoveredHash = null;
        foreach (string root in packageRoots)
        {
            string metadataPath = Path.Combine(
                Path.GetFullPath(root),
                parts[0].ToLowerInvariant(),
                parts[1].ToLowerInvariant(),
                ".nupkg.metadata");
            if (!File.Exists(metadataPath) || HasReparsePointBelowRoot(metadataPath, root))
                continue;

            try
            {
                // NuGet records the restore digest here. The package catalog later rechecks both
                // the archive content hash and the extracted input against that archive.
                using JsonDocument metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
                if (!metadata.RootElement.TryGetProperty("contentHash", out JsonElement contentHash) ||
                    contentHash.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(contentHash.GetString()))
                {
                    continue;
                }

                string value = contentHash.GetString()!;
                if (discoveredHash is not null &&
                    !string.Equals(discoveredHash, value, StringComparison.Ordinal))
                {
                    hashes[packageKey] = string.Empty;
                    return;
                }
                discoveredHash = value;
            }
            catch
            {
                hashes[packageKey] = string.Empty;
                return;
            }
        }

        AddPackageHash(packageKey, discoveredHash, hashes);
        AddSdkManagedPackageKey(packageKey, discoveredHash, hashes, sdkManagedPackageKeys);
    }

    private static void AddSdkManagedPackageKey(
        string packageKey,
        string? expectedHash,
        IReadOnlyDictionary<string, string> hashes,
        HashSet<string> sdkManagedPackageKeys)
    {
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            hashes.TryGetValue(packageKey, out string? actualHash) &&
            string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            sdkManagedPackageKeys.Add(packageKey);
        }
    }

    private static void AddPackageHash(
        string packageKey,
        string? value,
        Dictionary<string, string> hashes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (hashes.TryGetValue(packageKey, out string? existing) &&
            !string.Equals(existing, value, StringComparison.Ordinal))
        {
            hashes[packageKey] = string.Empty;
        }
        else
        {
            hashes[packageKey] = value!;
        }
    }
}
