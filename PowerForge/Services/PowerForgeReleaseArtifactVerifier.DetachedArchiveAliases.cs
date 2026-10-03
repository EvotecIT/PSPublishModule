using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private static bool DeclaresDetachedArchiveEvidence(JsonElement entry)
    {
        if (!TryGet(entry, "EvidencePaths", out JsonElement declared) || declared.ValueKind == JsonValueKind.Null)
            return false;
        if (declared.ValueKind != JsonValueKind.Array || declared.GetArrayLength() == 0)
            throw Invalid("Portable manifest has invalid detached evidence paths.");
        string[] paths = declared.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .ToArray();
        if (paths.Length != 2 ||
            !paths.Any(path => path.EndsWith(PowerForgePortablePayloadInventory.DirectInventorySuffix, StringComparison.OrdinalIgnoreCase)) ||
            !paths.Any(path => path.EndsWith(PowerForgePortablePayloadInventory.DirectSignatureSuffix, StringComparison.OrdinalIgnoreCase)))
            throw Invalid("Portable manifest does not declare a complete detached evidence pair.");
        return true;
    }

    private static string ResolveDetachedPortableAlias(
        string projectRoot,
        string checksumsPath,
        JsonElement selectedEntry,
        string artifactExtension,
        bool allowOutsideProjectRoot)
    {
        string[] candidates = DotNetPublishReleaseArtifactVerifier
            .FindChecksumPathsBySuffix(checksumsPath, PowerForgePortablePayloadInventory.DirectInventorySuffix)
            .Where(path => path.EndsWith(artifactExtension + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                StringComparison.OrdinalIgnoreCase))
            .Select(path => ResolveManifestPath(projectRoot, path, allowOutsideProjectRoot))
            .Where(File.Exists)
            .Select(inventoryPath => inventoryPath.Substring(0,
                inventoryPath.Length - PowerForgePortablePayloadInventory.DirectInventorySuffix.Length))
            .Where(archivePath => File.Exists(archivePath) &&
                                  File.Exists(archivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix))
            .Where(archivePath => DetachedInventoryMatchesManifest(
                projectRoot, checksumsPath, archivePath, selectedEntry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (candidates.Length != 1)
            throw Invalid("Renamed portable artifact must resolve to exactly one checksummed detached-evidence asset matching the selected manifest entry.");
        return candidates[0];
    }

    private static bool DetachedInventoryMatchesManifest(
        string projectRoot,
        string checksumsPath,
        string archivePath,
        JsonElement entry)
    {
        string inventoryPath = archivePath + PowerForgePortablePayloadInventory.DirectInventorySuffix;
        string signaturePath = archivePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix;
        VerifyChecksummedFile(projectRoot, checksumsPath, inventoryPath, "portable inventory");
        VerifyChecksummedFile(projectRoot, checksumsPath, signaturePath, "portable inventory signature");
        PowerForgePortablePayloadInventory? inventory;
        try
        {
            inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(
                ReadBoundedFileBytes(inventoryPath, "Portable payload inventory"));
        }
        catch (JsonException)
        {
            return false;
        }
        if (inventory is null)
            return false;
        return string.Equals(inventory.Target, ReadString(entry, "Target"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inventory.BundleId ?? string.Empty, ReadString(entry, "BundleId"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inventory.Runtime, ReadString(entry, "Runtime"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inventory.Framework, ReadString(entry, "Framework"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inventory.Style, ReadString(entry, "Style"), StringComparison.OrdinalIgnoreCase) &&
               string.Equals(inventory.SourceRevision, ReadString(entry, "SourceRevision"), StringComparison.OrdinalIgnoreCase);
    }
}
