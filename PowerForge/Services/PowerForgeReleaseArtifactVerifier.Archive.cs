using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private const long MaxArchiveMetadataBytes = 4L * 1024L * 1024L;
    private const long MaxModuleSignedEntryBytes = 512L * 1024L * 1024L;
    private const long MaxModuleSignedEntriesBytes = 2L * 1024L * 1024L * 1024L;

    private PortableArchiveVerification VerifyPortableArchiveInventory(
        string archivePath,
        string? expectedThumbprint,
        string? expectedSubject)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        if (!entries.TryGetValue(PowerForgePortablePayloadInventory.InventoryFileName, out ZipArchiveEntry? inventoryEntry) ||
            !entries.TryGetValue(PowerForgePortablePayloadInventory.SignatureFileName, out ZipArchiveEntry? signatureEntry))
            throw Invalid("Portable archive is missing its publisher-signed payload inventory.");
        byte[] inventoryBytes = ReadBoundedEntryBytes(inventoryEntry, "Portable payload inventory");
        byte[] signatureBytes = ReadBoundedEntryBytes(signatureEntry, "Portable payload inventory signature");
        PowerForgePayloadInventorySignature inventorySignature;
        try
        {
            inventorySignature = _verifyPortableInventory(inventoryBytes, signatureBytes);
        }
        catch (Exception exception) when (exception is CryptographicException || exception is InvalidDataException)
        {
            throw Invalid($"Portable payload inventory signature is not valid: {exception.Message}");
        }
        if (expectedThumbprint is not null && !string.Equals(
                inventorySignature.Thumbprint,
                expectedThumbprint,
                StringComparison.OrdinalIgnoreCase))
            throw Invalid("Portable payload inventory does not use the configured release certificate.");
        if (expectedThumbprint is null && expectedSubject is not null &&
            !DotNetPublishReleaseArtifactVerifier.CertificateSubjectsEqual(inventorySignature.Subject, expectedSubject))
            throw Invalid("Portable payload inventory does not match the configured release certificate subject.");
        PowerForgePortablePayloadInventory inventory;
        try
        {
            inventory = JsonSerializer.Deserialize<PowerForgePortablePayloadInventory>(inventoryBytes)
                ?? throw Invalid("Portable payload inventory could not be deserialized.");
        }
        catch (JsonException exception)
        {
            throw Invalid($"Portable payload inventory is not valid JSON: {exception.Message}");
        }
        if (inventory.SchemaVersion != 2)
            throw Invalid("Portable payload inventory schema version is not supported.");
        var represented = new Dictionary<string, PowerForgePortablePayloadEntry>(StringComparer.Ordinal);
        foreach (PowerForgePortablePayloadEntry representedEntry in inventory.Entries ?? Array.Empty<PowerForgePortablePayloadEntry>())
        {
            string representedPath = NormalizeArchivePath(representedEntry.Path);
            if (represented.ContainsKey(representedPath))
                throw Invalid($"Portable payload inventory contains duplicate entry '{representedPath}'.");
            represented.Add(representedPath, representedEntry);
        }
        string[] payloadPaths = entries.Keys
            .Where(path => !string.Equals(path, PowerForgePortablePayloadInventory.InventoryFileName, StringComparison.Ordinal) &&
                           !string.Equals(path, PowerForgePortablePayloadInventory.SignatureFileName, StringComparison.Ordinal))
            .ToArray();
        if (represented.Count != payloadPaths.Length || payloadPaths.Any(path => !represented.ContainsKey(path)))
            throw Invalid("Portable archive entries do not exactly match the publisher-signed payload inventory.");
        foreach (string path in payloadPaths)
        {
            PowerForgePortablePayloadEntry expected = represented[path];
            ZipArchiveEntry entry = entries[path];
            if (entry.Length != expected.Length || !string.Equals(
                    ComputeSha256(entry),
                    expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw Invalid($"Portable archive entry '{path}' does not match the publisher-signed payload inventory.");
        }

        string[] declaredSignedPaths = inventory.SignedFilePaths ?? Array.Empty<string>();
        string[] signedPaths = declaredSignedPaths
            .Select(NormalizeArchivePath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (signedPaths.Length == 0 || signedPaths.Length != declaredSignedPaths.Length ||
            signedPaths.Any(path => !entries.ContainsKey(path)))
            throw Invalid("Portable payload inventory does not contain a complete signed-file selection.");
        string executablePath = NormalizeArchivePath(inventory.ExecutablePath);
        if (!signedPaths.Contains(executablePath, StringComparer.Ordinal))
            throw Invalid("Portable payload inventory executable is absent from its signed-file selection.");

        var signatures = new List<VerifiedSignature>();
        string? signedVersion = null;
        string? executableIdentity = null;
        string tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge.PortableInventory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            for (int index = 0; index < signedPaths.Length; index++)
            {
                string path = signedPaths[index];
                ZipArchiveEntry entry = entries[path];
                string extracted = Path.Combine(tempRoot, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Path.GetFileName(path));
                using (Stream input = entry.Open())
                using (FileStream output = File.Create(extracted))
                    CopyBounded(input, output, entry.Length, $"Portable signed entry '{path}'");
                VerifiedSignature signature = VerifySignature(extracted, expectedThumbprint, expectedSubject);
                signatures.Add(new VerifiedSignature(
                    signature.PhysicalPath,
                    archivePath + "!" + path,
                    signature.Subject,
                    signature.Thumbprint));
                if (string.Equals(path, executablePath, StringComparison.Ordinal))
                {
                    signedVersion = _readPortableVersion(extracted);
                    executableIdentity = _readPortableIdentity(extracted);
                }
            }
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        VerifiedSignature payloadSigner = RequireOneSigner(signatures);
        if (!string.Equals(
                inventorySignature.Thumbprint,
                payloadSigner.Thumbprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid(
                "Portable payload inventory signature does not use the Authenticode publisher certificate.");
        }

        return new PortableArchiveVerification(
            inventory,
            inventorySignature,
            signatures.ToArray(),
            signedVersion ?? throw Invalid("Portable payload inventory executable version is missing."),
            executableIdentity ?? throw Invalid("Portable payload inventory executable identity is missing."));
    }

    private sealed class PortableArchiveVerification
    {
        internal PortableArchiveVerification(
            PowerForgePortablePayloadInventory inventory,
            PowerForgePayloadInventorySignature inventorySignature,
            VerifiedSignature[] signatures,
            string signedProductVersion,
            string executableIdentity)
        {
            Inventory = inventory;
            InventorySignature = inventorySignature;
            Signatures = signatures;
            SignedProductVersion = signedProductVersion;
            ExecutableIdentity = executableIdentity;
        }

        internal PowerForgePortablePayloadInventory Inventory { get; }
        internal PowerForgePayloadInventorySignature InventorySignature { get; }
        internal VerifiedSignature[] Signatures { get; }
        internal string SignedProductVersion { get; }
        internal string ExecutableIdentity { get; }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizeArchivePath(entry.FullName);
            if (!duplicateGuard.Add(normalized))
                throw Invalid($"Release archive contains duplicate entry '{normalized}'.");
            if (normalized.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                continue;
            entries.Add(normalized, entry);
        }
        return entries;
    }

    private static string NormalizeArchivePath(string? value)
    {
        string path = DotNetPublishReleaseArtifactVerifier.RequireText(value, "archive entry path").Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw Invalid($"Release archive contains unsafe entry '{path}'.");
        string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
            throw Invalid($"Release archive contains unsafe entry '{path}'.");
        return string.Join("/", segments);
    }

    private static byte[] ReadBoundedEntryBytes(ZipArchiveEntry entry, string label)
    {
        if (entry.Length < 0 || entry.Length > MaxArchiveMetadataBytes)
            throw Invalid($"{label} exceeds the {MaxArchiveMetadataBytes} byte metadata limit.");
        using Stream input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        CopyBounded(input, output, MaxArchiveMetadataBytes, label);
        return output.ToArray();
    }

    private static byte[] ReadBoundedFileBytes(string path, string label)
    {
        var info = new FileInfo(path);
        if (info.Length < 0 || info.Length > MaxArchiveMetadataBytes)
            throw Invalid($"{label} exceeds the {MaxArchiveMetadataBytes} byte metadata limit.");
        using FileStream input = File.OpenRead(path);
        using var output = new MemoryStream((int)info.Length);
        CopyBounded(input, output, MaxArchiveMetadataBytes, label);
        return output.ToArray();
    }

    private static void CopyBounded(Stream input, Stream output, long maximumBytes, string label)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
                throw Invalid($"{label} exceeds the {maximumBytes} byte limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static string ComputeSha256(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
    }
}
