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

    public int SchemaVersion { get; set; }
    public string ArtifactId { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? BundleId { get; set; }
    public string Runtime { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Style { get; set; } = string.Empty;
    public string SourceRevision { get; set; } = string.Empty;
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
    internal PowerForgePayloadInventorySignature(string subject, string thumbprint)
    {
        Subject = subject;
        Thumbprint = thumbprint;
    }

    internal string Subject { get; }
    internal string Thumbprint { get; }
}

internal static class PowerForgePortablePayloadInventoryCms
{
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
        cms.CheckSignature(verifySignatureOnly: true);
        if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is null)
            throw new InvalidDataException("Portable payload inventory must have exactly one certificate-backed signature.");
        X509Certificate2 certificate = cms.SignerInfos[0].Certificate!;
        return new PowerForgePayloadInventorySignature(
            certificate.Subject,
            DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(certificate.Thumbprint));
    }

    internal static PowerForgePortablePayloadInventory Create(
        string outputDirectory,
        string artifactId,
        string runtime,
        string framework,
        string style,
        string sourceRevision,
        string executablePath,
        string executableIdentity,
        string version,
        IEnumerable<string> signedFilePaths,
        string? bundleId = null)
    {
        string root = Path.GetFullPath(outputDirectory);
        PowerForgePortablePayloadEntry[] entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !IsRootMetadataPath(NormalizeRelative(root, path)))
            .Select(path => new PowerForgePortablePayloadEntry
            {
                Path = NormalizeRelative(root, path),
                Length = new FileInfo(path).Length,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(path).ToLowerInvariant()
            })
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();
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
            SchemaVersion = 3,
            ArtifactId = artifactId,
            Target = artifactId,
            BundleId = string.IsNullOrWhiteSpace(bundleId) ? null : bundleId!.Trim(),
            Runtime = runtime,
            Framework = framework,
            Style = style,
            SourceRevision = sourceRevision.ToLowerInvariant(),
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
