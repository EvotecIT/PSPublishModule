using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private static void ValidateSbom(
        string path,
        byte[] content,
        string artifactId,
        string artifactVersion,
        string artifactDigest)
    {
        using JsonDocument document = ParseSbomJson(content);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw Invalid($"SBOM '{Path.GetFileName(path)}' is not a recognizable CycloneDX or SPDX JSON document.");

        if (string.Equals(ReadString(root, "bomFormat"), "CycloneDX", StringComparison.OrdinalIgnoreCase))
        {
            string specificationVersion = ReadString(root, "specVersion");
            string serialNumber = ReadString(root, "serialNumber");
            int version = ReadInt32(root, "version");
            bool supportedVersion = specificationVersion == "1.4" ||
                                    specificationVersion == "1.5" ||
                                    specificationVersion == "1.6";
            bool validSerialNumber = string.IsNullOrWhiteSpace(serialNumber) ||
                                     (Uri.TryCreate(serialNumber, UriKind.Absolute, out Uri? serialUri) &&
                                      string.Equals(serialUri.Scheme, "urn", StringComparison.OrdinalIgnoreCase));
            if (!supportedVersion || version < 1 || !validSerialNumber ||
                (!TryGet(root, "components", out JsonElement components) || components.ValueKind != JsonValueKind.Array) &&
                (!TryGet(root, "metadata", out JsonElement metadata) || metadata.ValueKind != JsonValueKind.Object))
                throw Invalid($"CycloneDX SBOM '{Path.GetFileName(path)}' is missing supported document-level fields.");
            if (!EnumerateCycloneDxComponents(root).Any(component =>
                    Is(component, "name", artifactId) &&
                    Is(component, "version", artifactVersion) &&
                    HasCycloneDxSha256(component, artifactDigest)))
                throw Invalid($"CycloneDX SBOM '{Path.GetFileName(path)}' does not bind the admitted artifact identity, version, and SHA-256 digest.");
            return;
        }

        if (ReadString(root, "spdxVersion").StartsWith("SPDX-", StringComparison.OrdinalIgnoreCase))
        {
            string specificationVersion = ReadString(root, "spdxVersion");
            bool supportedVersion = specificationVersion == "SPDX-2.2" || specificationVersion == "SPDX-2.3";
            string dataLicense = ReadString(root, "dataLicense");
            string documentNamespace = ReadString(root, "documentNamespace");
            if (!supportedVersion || !string.Equals(dataLicense, "CC0-1.0", StringComparison.OrdinalIgnoreCase) ||
                ReadString(root, "SPDXID") != "SPDXRef-DOCUMENT" ||
                string.IsNullOrWhiteSpace(ReadString(root, "name")) ||
                !Uri.TryCreate(documentNamespace, UriKind.Absolute, out _) ||
                !TryGet(root, "creationInfo", out JsonElement creationInfo) || creationInfo.ValueKind != JsonValueKind.Object ||
                string.IsNullOrWhiteSpace(ReadString(creationInfo, "created")) ||
                !TryGet(creationInfo, "creators", out JsonElement creators) || creators.ValueKind != JsonValueKind.Array ||
                creators.GetArrayLength() == 0)
                throw Invalid($"SPDX SBOM '{Path.GetFileName(path)}' is missing supported document-level fields.");
            if (!TryGet(root, "packages", out JsonElement packages) || packages.ValueKind != JsonValueKind.Array ||
                !packages.EnumerateArray().Any(package =>
                    Is(package, "name", artifactId) &&
                    Is(package, "versionInfo", artifactVersion) &&
                    HasSpdxSha256(package, artifactDigest)))
                throw Invalid($"SPDX SBOM '{Path.GetFileName(path)}' does not bind the admitted artifact identity, version, and SHA-256 digest.");
            return;
        }

        throw Invalid($"SBOM '{Path.GetFileName(path)}' is not a recognizable CycloneDX or SPDX JSON document.");
    }

    private static JsonDocument ParseSbomJson(byte[] content)
    {
        try
        {
            return JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException exception)
        {
            throw Invalid($"SBOM is not valid JSON: {exception.Message}");
        }
    }

    private static IEnumerable<JsonElement> EnumerateCycloneDxComponents(JsonElement root)
    {
        if (TryGet(root, "metadata", out JsonElement metadata) &&
            TryGet(metadata, "component", out JsonElement metadataComponent) &&
            metadataComponent.ValueKind == JsonValueKind.Object)
            yield return metadataComponent;
        if (!TryGet(root, "components", out JsonElement components) || components.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (JsonElement component in components.EnumerateArray())
        {
            if (component.ValueKind == JsonValueKind.Object)
                yield return component;
        }
    }

    private static bool HasCycloneDxSha256(JsonElement component, string digest)
    {
        if (!TryGet(component, "hashes", out JsonElement hashes) || hashes.ValueKind != JsonValueKind.Array)
            return false;
        return hashes.EnumerateArray().Any(hash =>
            (Is(hash, "alg", "SHA-256") || Is(hash, "alg", "SHA256")) &&
            DigestsEqual(ReadString(hash, "content"), digest));
    }

    private static bool HasSpdxSha256(JsonElement package, string digest)
    {
        if (!TryGet(package, "checksums", out JsonElement checksums) || checksums.ValueKind != JsonValueKind.Array)
            return false;
        return checksums.EnumerateArray().Any(checksum =>
            Is(checksum, "algorithm", "SHA256") &&
            DigestsEqual(ReadString(checksum, "checksumValue"), digest));
    }

    private static bool DigestsEqual(string left, string right) =>
        string.Equals(NormalizeDigest(left), NormalizeDigest(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeDigest(string value) =>
        (value ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
}
