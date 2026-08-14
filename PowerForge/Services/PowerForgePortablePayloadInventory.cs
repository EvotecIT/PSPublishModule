using System.Formats.Asn1;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace PowerForge;

internal sealed class PowerForgePortablePayloadInventory
{
    internal const string InventoryFileName = "PowerForge.ReleaseInventory.json";
    internal const string SignatureFileName = "PowerForge.ReleaseInventory.p7s";
    internal const string DirectInventorySuffix = ".release-inventory.json";
    internal const string DirectSignatureSuffix = ".release-inventory.p7s";

    public int SchemaVersion { get; set; }
    public string ArtifactId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? BundleId { get; set; }
    public string Runtime { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
    public bool SourceDirty { get; set; }
    public string ConfigurationPolicySha256 { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string ExecutableIdentity { get; set; } = string.Empty;
    public string[] SignedFilePaths { get; set; } = Array.Empty<string>();
    public PowerForgePortablePayloadEntry[] Entries { get; set; } = Array.Empty<PowerForgePortablePayloadEntry>();
}

internal sealed class PowerForgePortablePayloadEntry
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed class PowerForgePayloadInventorySignature
{
    internal PowerForgePayloadInventorySignature(
        string subject,
        string thumbprint,
        bool certificateTrusted)
    {
        Subject = subject;
        Thumbprint = thumbprint;
        CertificateTrusted = certificateTrusted;
    }

    internal string Subject { get; }
    internal string Thumbprint { get; }
    internal bool CertificateTrusted { get; }
}

internal static class PowerForgePortablePayloadInventoryCms
{
    private const string Pkcs7DataContentTypeOid = "1.2.840.113549.1.7.1";
    private const string CodeSigningEkuOid = "1.3.6.1.5.5.7.3.3";
    private const string TimestampingEkuOid = "1.3.6.1.5.5.7.3.8";
    private const string MicrosoftRfc3161TimestampOid = "1.3.6.1.4.1.311.3.3.1";
    private const string SignatureTimestampTokenOid = "1.2.840.113549.1.9.16.2.14";
    private const string Rfc3161TimestampTokenContentTypeOid = "1.2.840.113549.1.9.16.1.4";

    internal static (string InventoryPath, string SignaturePath) ResolveEvidencePaths(
        string outputDirectory,
        string executablePath,
        bool archivePayload)
        => archivePayload
            ? (
                Path.Combine(outputDirectory, PowerForgePortablePayloadInventory.InventoryFileName),
                Path.Combine(outputDirectory, PowerForgePortablePayloadInventory.SignatureFileName))
            : (
                executablePath + PowerForgePortablePayloadInventory.DirectInventorySuffix,
                executablePath + PowerForgePortablePayloadInventory.DirectSignatureSuffix);

    internal static void EnsureEvidencePathsAvailable(string inventoryPath, string signaturePath)
    {
        foreach (string path in new[] { inventoryPath, signaturePath })
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new InvalidOperationException(
                    $"Portable payload contains reserved release-inventory metadata path '{path}'.");
            }
        }
    }

    internal static void WriteEvidenceFiles(
        string inventoryPath,
        byte[] inventoryBytes,
        string signaturePath,
        byte[] signatureBytes)
    {
        bool inventoryCreated = false;
        bool signatureCreated = false;
        try
        {
            using (var inventory = new FileStream(inventoryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                inventoryCreated = true;
                inventory.Write(inventoryBytes, 0, inventoryBytes.Length);
            }
            using (var signature = new FileStream(signaturePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                signatureCreated = true;
                signature.Write(signatureBytes, 0, signatureBytes.Length);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            if (inventoryCreated)
            {
                try { File.Delete(inventoryPath); } catch { }
            }
            if (signatureCreated)
            {
                try { File.Delete(signaturePath); } catch { }
            }
            throw new InvalidOperationException(
                "Portable release-inventory evidence path appeared while evidence was being created.",
                exception);
        }
    }
    internal static byte[] Sign(byte[] content, DotNetPublishSignOptions options)
    {
        X509Certificate2 certificate = FindSigningCertificate(options);
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.ComputeSignature(new CmsSigner(certificate) { IncludeOption = X509IncludeOption.EndCertOnly });
        return cms.Encode();
    }

    internal static PowerForgePayloadInventorySignature Verify(byte[] content, byte[] signature)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: true);
        cms.Decode(signature);
        if (!string.Equals(cms.ContentInfo.ContentType.Value, Pkcs7DataContentTypeOid, StringComparison.Ordinal))
            throw new InvalidDataException("Portable payload inventory signature must use the PKCS#7 data content type.");
        cms.CheckSignature(verifySignatureOnly: true);
        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is null)
            throw new InvalidDataException("Portable payload inventory must have exactly one certificate-backed signature.");
        X509Certificate2 certificate = cms.SignerInfos[0].Certificate!;
        return new PowerForgePayloadInventorySignature(
            certificate.Subject,
            DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(certificate.Thumbprint),
            ValidateCertificateTrust(cms, cms.SignerInfos[0], certificate));
    }

    private static bool ValidateCertificateTrust(
        SignedCms cms,
        SignerInfo signerInfo,
        X509Certificate2 signerCertificate)
    {
        DateTime verificationTime = DateTime.UtcNow;
        X509Certificate2Collection extraStore = cms.Certificates;
        if (TryGetTrustedTimestamp(signerInfo, cms.Certificates, out DateTime timestamp, out X509Certificate2Collection timestampCertificates))
        {
            verificationTime = timestamp;
            foreach (X509Certificate2 certificate in timestampCertificates)
                extraStore.Add(certificate);
        }

        return BuildTrustedChain(
            signerCertificate,
            extraStore,
            verificationTime,
            CodeSigningEkuOid);
    }

    private static bool TryGetTrustedTimestamp(
        SignerInfo signerInfo,
        X509Certificate2Collection extraCandidates,
        out DateTime timestamp,
        out X509Certificate2Collection timestampCertificates)
    {
        timestamp = default;
        timestampCertificates = new X509Certificate2Collection();
        foreach (CryptographicAttributeObject attribute in signerInfo.UnsignedAttributes)
        {
            if (!string.Equals(attribute.Oid?.Value, MicrosoftRfc3161TimestampOid, StringComparison.Ordinal) &&
                !string.Equals(attribute.Oid?.Value, SignatureTimestampTokenOid, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (AsnEncodedData value in attribute.Values)
            {
#if NET472
                if (!TryDecodeTimestampForSignerInfo(
                        value.RawData,
                        signerInfo,
                        out DateTime candidateTime,
                        out X509Certificate2? timestampSigner,
                        out X509Certificate2Collection candidateCertificates) ||
                    timestampSigner is null ||
                    !BuildTrustedChain(
                        timestampSigner,
                        candidateCertificates,
                        candidateTime,
                        TimestampingEkuOid))
                {
                    continue;
                }

                timestamp = candidateTime;
                timestampCertificates = candidateCertificates;
                return true;
#else
                if (!Rfc3161TimestampToken.TryDecode(value.RawData, out Rfc3161TimestampToken? token, out int bytesConsumed) ||
                    token is null ||
                    bytesConsumed != value.RawData.Length ||
                    !token.VerifySignatureForSignerInfo(signerInfo, out X509Certificate2? timestampSigner, extraCandidates) ||
                    timestampSigner is null)
                {
                    continue;
                }

                SignedCms timestampCms = token.AsSignedCms();
                timestampCertificates = timestampCms.Certificates;
                DateTime candidateTime = token.TokenInfo.Timestamp.UtcDateTime;
                if (!BuildTrustedChain(
                        timestampSigner,
                        timestampCertificates,
                        candidateTime,
                        TimestampingEkuOid))
                {
                    continue;
                }

                timestamp = candidateTime;
                return true;
#endif
            }
        }

        return false;
    }

    internal static bool TryDecodeTimestampForSignerInfo(
        byte[] encodedTimestamp,
        SignerInfo signerInfo,
        out DateTime timestamp,
        out X509Certificate2? timestampSigner,
        out X509Certificate2Collection timestampCertificates)
    {
        timestamp = default;
        timestampSigner = null;
        timestampCertificates = new X509Certificate2Collection();

        try
        {
            var timestampCms = new SignedCms();
            timestampCms.Decode(encodedTimestamp);
            if (!string.Equals(
                    timestampCms.ContentInfo.ContentType.Value,
                    Rfc3161TimestampTokenContentTypeOid,
                    StringComparison.Ordinal) ||
                timestampCms.SignerInfos.Count != 1 ||
                timestampCms.SignerInfos[0].Certificate is null)
            {
                return false;
            }

            timestampCms.CheckSignature(verifySignatureOnly: true);
            if (!TryReadTimestampInfo(
                    timestampCms.ContentInfo.Content,
                    out DateTime candidateTime,
                    out string digestOid,
                    out byte[] expectedDigest))
            {
                return false;
            }

            byte[] actualDigest = ComputeDigest(digestOid, signerInfo.GetSignature());
            if (!FixedTimeEquals(actualDigest, expectedDigest))
                return false;

            timestamp = candidateTime;
            timestampSigner = timestampCms.SignerInfos[0].Certificate;
            timestampCertificates = timestampCms.Certificates;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (AsnContentException)
        {
            return false;
        }
    }

    private static bool TryReadTimestampInfo(
        byte[] encodedTimestampInfo,
        out DateTime timestamp,
        out string digestOid,
        out byte[] digest)
    {
        timestamp = default;
        digestOid = string.Empty;
        digest = Array.Empty<byte>();

        var reader = new AsnReader(encodedTimestampInfo, AsnEncodingRules.DER);
        AsnReader sequence = reader.ReadSequence();
        _ = sequence.ReadInteger();
        _ = sequence.ReadObjectIdentifier();
        AsnReader messageImprint = sequence.ReadSequence();
        AsnReader algorithm = messageImprint.ReadSequence();
        digestOid = algorithm.ReadObjectIdentifier();
        if (algorithm.HasData)
            _ = algorithm.ReadEncodedValue();
        if (algorithm.HasData)
            return false;
        digest = messageImprint.ReadOctetString();
        if (messageImprint.HasData)
            return false;
        _ = sequence.ReadInteger();
        timestamp = sequence.ReadGeneralizedTime().UtcDateTime;
        return !reader.HasData && digest.Length > 0;
    }

    private static byte[] ComputeDigest(string digestOid, byte[] content)
    {
        using HashAlgorithm algorithm = digestOid switch
        {
            "1.3.14.3.2.26" => SHA1.Create(),
            "2.16.840.1.101.3.4.2.1" => SHA256.Create(),
            "2.16.840.1.101.3.4.2.2" => SHA384.Create(),
            "2.16.840.1.101.3.4.2.3" => SHA512.Create(),
            _ => throw new CryptographicException($"Unsupported RFC 3161 digest algorithm '{digestOid}'.")
        };
        return algorithm.ComputeHash(content);
    }

    private static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        int difference = 0;
        for (int index = 0; index < left.Length; index++)
            difference |= left[index] ^ right[index];
        return difference == 0;
    }

    private static bool BuildTrustedChain(
        X509Certificate2 certificate,
        X509Certificate2Collection extraStore,
        DateTime verificationTime,
        string applicationPolicyOid)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        chain.ChainPolicy.VerificationTime = verificationTime;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(applicationPolicyOid));
        foreach (X509Certificate2 candidate in extraStore)
        {
            if (!string.Equals(candidate.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                chain.ChainPolicy.ExtraStore.Add(candidate);
        }
        return chain.Build(certificate);
    }

    internal static PowerForgePortablePayloadInventory Create(
        string outputDirectory,
        string artifactId,
        string runtime,
        string framework,
        string style,
        string sourceRevision,
        string configurationPolicySha256,
        string executablePath,
        string executableIdentity,
        string version,
        IEnumerable<string> signedFilePaths,
        string? bundleId = null,
        bool sourceDirty = false,
        bool includeCompleteOutput = true)
    {
        string root = Path.GetFullPath(outputDirectory);
        IEnumerable<string> payloadPaths = includeCompleteOutput
            ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            : new[] { executablePath };
        PowerForgePortablePayloadEntry[] entries = payloadPaths
            .Where(path => !IsRootMetadataPath(NormalizeRelative(root, path)))
            .Select(path => new PowerForgePortablePayloadEntry
            {
                Path = NormalizeRelative(root, path),
                Length = new FileInfo(path).Length,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(path).ToLowerInvariant()
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
        return CreateCore(
            root,
            artifactId,
            runtime,
            framework,
            style,
            sourceRevision,
            configurationPolicySha256,
            executablePath,
            executableIdentity,
            version,
            signedFilePaths,
            entries,
            bundleId,
            sourceDirty);
    }

    internal static PowerForgePortablePayloadInventory CreateFromArchive(
        string archivePath,
        string outputDirectory,
        string artifactId,
        string runtime,
        string framework,
        string style,
        string sourceRevision,
        string configurationPolicySha256,
        string executablePath,
        string executableIdentity,
        string version,
        IEnumerable<string> signedFilePaths,
        string? bundleId = null,
        bool sourceDirty = false,
        bool requireSignedDlls = false)
    {
        string root = Path.GetFullPath(outputDirectory);
        var entries = new List<PowerForgePortablePayloadEntry>();
        var archiveEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (ZipArchive archive = ZipFile.OpenRead(archivePath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalized = NormalizeArchivePath(entry.FullName);
                if (!duplicateGuard.Add(normalized))
                    throw new InvalidOperationException($"Portable archive contains duplicate entry '{normalized}'.");
                if (normalized.Length == 0 ||
                    entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                    entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                {
                    continue;
                }
                if (IsRootMetadataPath(normalized))
                    continue;
                if (string.Equals(normalized, PowerForgePortablePayloadInventory.InventoryFileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, PowerForgePortablePayloadInventory.SignatureFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Portable archive contains non-canonical reserved release-inventory path '{normalized}'.");
                }

                string digest = ComputeSha256(entry);
                entries.Add(new PowerForgePortablePayloadEntry
                {
                    Path = normalized,
                    Length = entry.Length,
                    Sha256 = digest.ToLowerInvariant()
                });
                archiveEntries.Add(normalized, entry);
            }

            string[] materializedSignedPaths = signedFilePaths.ToArray();
            foreach (string signedPath in materializedSignedPaths)
            {
                string relativePath = NormalizeRelative(root, signedPath);
                if (!archiveEntries.TryGetValue(relativePath, out ZipArchiveEntry? archiveEntry) ||
                    archiveEntry.Length != new FileInfo(signedPath).Length ||
                    !string.Equals(
                        ComputeSha256(archiveEntry),
                        DotNetPublishReleaseArtifactVerifier.ComputeSha256(signedPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Portable archive signed entry '{relativePath}' changed after publisher signing.");
                }
            }

            var signedArchivePaths = new HashSet<string>(
                materializedSignedPaths.Select(path => NormalizeRelative(root, path)),
                StringComparer.Ordinal);
            string[] unsignedPortableBinaries = archiveEntries.Keys
                .Where(path => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                               (requireSignedDlls && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                .Where(path => !signedArchivePaths.Contains(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (unsignedPortableBinaries.Length > 0)
            {
                throw new InvalidOperationException(
                    "Portable archive contains executable payloads that were added or changed after publisher signing: " +
                    string.Join(", ", unsignedPortableBinaries));
            }

            return CreateCore(
                root,
                artifactId,
                runtime,
                framework,
                style,
                sourceRevision,
                configurationPolicySha256,
                executablePath,
                executableIdentity,
                version,
                materializedSignedPaths,
                entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray(),
                bundleId,
                sourceDirty);
        }
    }

    internal static void RewriteArchiveEvidence(
        string archivePath,
        byte[] inventoryBytes,
        byte[] signatureBytes)
    {
        using ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        foreach (string reservedPath in new[]
                 {
                     PowerForgePortablePayloadInventory.InventoryFileName,
                     PowerForgePortablePayloadInventory.SignatureFileName
                 })
        {
            ZipArchiveEntry[] matches = archive.Entries
                .Where(entry => string.Equals(entry.FullName, reservedPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Any(entry => !string.Equals(entry.FullName, reservedPath, StringComparison.Ordinal)) ||
                matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Portable archive contains ambiguous reserved release-inventory path '{reservedPath}'.");
            }
            foreach (ZipArchiveEntry match in matches)
                match.Delete();
        }

        WriteArchiveEntry(
            archive,
            PowerForgePortablePayloadInventory.InventoryFileName,
            inventoryBytes);
        WriteArchiveEntry(
            archive,
            PowerForgePortablePayloadInventory.SignatureFileName,
            signatureBytes);
    }

    internal static void RewriteEvidenceFiles(
        string inventoryPath,
        byte[] inventoryBytes,
        string signaturePath,
        byte[] signatureBytes)
    {
        if (Directory.Exists(inventoryPath) || Directory.Exists(signaturePath))
            throw new InvalidOperationException("Portable release-inventory evidence path is a directory.");
        if (File.Exists(inventoryPath)) File.Delete(inventoryPath);
        if (File.Exists(signaturePath)) File.Delete(signaturePath);
        WriteEvidenceFiles(inventoryPath, inventoryBytes, signaturePath, signatureBytes);
    }

    private static PowerForgePortablePayloadInventory CreateCore(
        string root,
        string artifactId,
        string runtime,
        string framework,
        string style,
        string sourceRevision,
        string configurationPolicySha256,
        string executablePath,
        string executableIdentity,
        string version,
        IEnumerable<string> signedFilePaths,
        PowerForgePortablePayloadEntry[] entries,
        string? bundleId,
        bool sourceDirty)
    {
        if (configurationPolicySha256.Length != 64 ||
            configurationPolicySha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                "Portable payload inventory requires a canonical configuration policy SHA-256 digest.");
        }

        string normalizedExecutablePath = NormalizeRelative(root, executablePath);
        string[] normalizedSignedPaths = signedFilePaths
            .Select(path => NormalizeRelative(root, path))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (!normalizedSignedPaths.Contains(normalizedExecutablePath, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "A signed portable payload must bind its primary executable to the configured publisher.");
        }
        return new PowerForgePortablePayloadInventory
        {
            SchemaVersion = 5,
            ArtifactId = artifactId,
            Target = artifactId,
            BundleId = string.IsNullOrWhiteSpace(bundleId) ? null : bundleId!.Trim(),
            Runtime = runtime,
            Framework = framework,
            Style = style,
            SourceRevision = sourceRevision.ToLowerInvariant(),
            SourceDirty = sourceDirty,
            ConfigurationPolicySha256 = configurationPolicySha256.ToLowerInvariant(),
            Version = version,
            ExecutablePath = normalizedExecutablePath,
            ExecutableIdentity = executableIdentity,
            SignedFilePaths = normalizedSignedPaths,
            Entries = entries
        };
    }

    internal static byte[] Serialize(PowerForgePortablePayloadInventory inventory) =>
        JsonSerializer.SerializeToUtf8Bytes(inventory, new JsonSerializerOptions { WriteIndented = true });

    private static string NormalizeRelative(string root, string path) =>
        DotNetPublishReleaseArtifactVerifier.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');

    private static bool IsRootMetadataPath(string relativePath) =>
        string.Equals(relativePath, PowerForgePortablePayloadInventory.InventoryFileName, StringComparison.Ordinal) ||
        string.Equals(relativePath, PowerForgePortablePayloadInventory.SignatureFileName, StringComparison.Ordinal);

    private static string NormalizeArchivePath(string value)
    {
        string path = (value ?? string.Empty).Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw new InvalidOperationException($"Portable archive contains unsafe entry '{path}'.");
        string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment == "." || segment == ".."))
            throw new InvalidOperationException($"Portable archive contains unsafe entry '{path}'.");
        return string.Join("/", segments);
    }

    private static string ComputeSha256(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using SHA256 sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(input)).Replace("-", string.Empty);
    }

    private static void WriteArchiveEntry(ZipArchive archive, string path, byte[] bytes)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using Stream output = entry.Open();
        output.Write(bytes, 0, bytes.Length);
    }

    private static X509Certificate2 FindSigningCertificate(DotNetPublishSignOptions options)
    {
        foreach (StoreLocation location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (X509Certificate2 certificate in store.Certificates)
            {
                if (!certificate.HasPrivateKey)
                    continue;
                if (!string.IsNullOrWhiteSpace(options.Thumbprint) && string.Equals(
                        DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(certificate.Thumbprint),
                        DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(options.Thumbprint),
                        StringComparison.OrdinalIgnoreCase))
                    return certificate;
                if (string.IsNullOrWhiteSpace(options.Thumbprint) &&
                    !string.IsNullOrWhiteSpace(options.SubjectName) &&
                    DotNetPublishReleaseArtifactVerifier.CertificateSubjectsEqual(certificate.Subject, options.SubjectName!))
                    return certificate;
            }
        }
        throw new InvalidOperationException("The portable inventory signing certificate with a private key was not found.");
    }
}
