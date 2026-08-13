using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Verifies signed portable CLI and PowerShell module artifacts against immutable hashes,
/// source provenance, and optional SBOM evidence.
/// </summary>
public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private readonly Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult> _verifyAuthenticode;
    private readonly Func<string, string> _readPortableVersion;

    /// <summary>Creates a verifier backed by WinTrust and signed-file version metadata.</summary>
    public PowerForgeReleaseArtifactVerifier()
        : this(DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode, ReadPortableVersion)
    {
    }

    internal PowerForgeReleaseArtifactVerifier(
        Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult> verifyAuthenticode,
        Func<string, string> readPortableVersion)
    {
        _verifyAuthenticode = verifyAuthenticode ?? throw new ArgumentNullException(nameof(verifyAuthenticode));
        _readPortableVersion = readPortableVersion ?? throw new ArgumentNullException(nameof(readPortableVersion));
    }

    /// <summary>Verifies one non-installer release artifact and returns hash-bound evidence.</summary>
    public PowerForgeReleaseArtifactEvidence Verify(PowerForgeReleaseArtifactVerificationRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        return request.Kind switch
        {
            PowerForgeReleaseArtifactKind.PortableCli => VerifyPortableCli(request),
            PowerForgeReleaseArtifactKind.PowerShellModule => VerifyPowerShellModule(request),
            _ => throw Invalid($"Unsupported release artifact kind '{request.Kind}'.")
        };
    }

    private PowerForgeReleaseEvidenceFile[] BuildExternalEvidence(
        string projectRoot,
        string checksumsPath,
        string? provenancePath,
        IEnumerable<string>? configurationPaths,
        IEnumerable<string>? sbomPaths)
    {
        var evidence = new List<PowerForgeReleaseEvidenceFile>
        {
            new()
            {
                Role = "checksums",
                Path = checksumsPath,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(checksumsPath)
            }
        };
        if (!string.IsNullOrWhiteSpace(provenancePath))
        {
            evidence.Add(new PowerForgeReleaseEvidenceFile
            {
                Role = "provenance",
                Path = provenancePath!,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(provenancePath!)
            });
        }
        foreach (string configurationPath in configurationPaths ?? Array.Empty<string>())
        {
            evidence.Add(new PowerForgeReleaseEvidenceFile
            {
                Role = "configuration",
                Path = configurationPath!,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(configurationPath!)
            });
        }

        foreach (string configuredPath in sbomPaths ?? Array.Empty<string>())
        {
            string path = ResolveRequestFile(projectRoot, configuredPath, "SBOM path");
            string digest = VerifyChecksummedFile(projectRoot, checksumsPath, path, "SBOM");
            ValidateSbom(path);
            evidence.Add(new PowerForgeReleaseEvidenceFile { Role = "sbom", Path = path, Sha256 = digest });
        }
        return evidence.ToArray();
    }

    private VerifiedSignature VerifySignature(string path, string? expectedThumbprint, string? expectedSubject)
    {
        DotNetPublishReleaseArtifactVerifier.AuthenticodeResult signature = _verifyAuthenticode(path);
        if (!signature.IsValid)
            throw Invalid($"Authenticode signature is not valid for '{Path.GetFileName(path)}' (0x{signature.StatusCode:X8}).");
        string thumbprint = DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature.Thumbprint);
        if (expectedThumbprint is not null &&
            !string.Equals(thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"'{Path.GetFileName(path)}' does not use the configured release certificate.");
        if (expectedThumbprint is null && expectedSubject is not null &&
            signature.Subject.IndexOf(expectedSubject, StringComparison.OrdinalIgnoreCase) < 0)
            throw Invalid($"'{Path.GetFileName(path)}' does not match the configured release certificate subject.");
        return new VerifiedSignature(path, path, signature.Subject, thumbprint);
    }

    private static VerifiedSignature RequireOneSigner(IReadOnlyList<VerifiedSignature> signatures)
    {
        if (signatures.Count == 0)
            throw Invalid("At least one valid release signature is required.");
        VerifiedSignature first = signatures[0];
        if (signatures.Any(signature =>
                !string.Equals(signature.Thumbprint, first.Thumbprint, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(signature.Subject, first.Subject, StringComparison.OrdinalIgnoreCase)))
            throw Invalid("All release signature evidence must use one publisher certificate.");
        return first;
    }

    private static string VerifyChecksummedFile(string projectRoot, string checksumsPath, string path, string label)
    {
        string fullPath = DotNetPublishReleaseArtifactVerifier.RequireFile(path, label);
        string relativePath = DotNetPublishReleaseArtifactVerifier.GetRelativePath(projectRoot, fullPath).Replace('\\', '/');
        string digest = DotNetPublishReleaseArtifactVerifier.ComputeSha256(fullPath);
        if (!DotNetPublishReleaseArtifactVerifier.ChecksumContains(checksumsPath, relativePath, digest))
            throw Invalid($"{label} SHA-256 does not match the PowerForge checksum catalog.");
        return digest;
    }

    private static void VerifyArchiveContainsFile(
        string archivePath,
        string outputDirectory,
        string representedPath,
        string expectedDigest)
    {
        string relative = DotNetPublishReleaseArtifactVerifier.GetRelativePath(outputDirectory, representedPath).Replace('\\', '/');
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        if (!entries.TryGetValue(NormalizeArchivePath(relative), out ZipArchiveEntry? entry) || entry.Length == 0)
            throw Invalid($"Portable archive does not contain signed file '{relative}'.");
        string digest = ComputeSha256(ReadEntryBytes(entry));
        if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Portable archive contains different bytes for signed file '{relative}'.");
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizeArchivePath(entry.FullName);
            if (normalized.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                continue;
            if (entries.ContainsKey(normalized))
                throw Invalid($"Release archive contains duplicate entry '{normalized}'.");
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

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
    }

    private static string ResolveManifestPath(string projectRoot, string value, bool allowOutsideProjectRoot)
    {
        string path = DotNetPublishReleaseArtifactVerifier.RequireText(value, "manifest artifact path")
            .Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(projectRoot, path));
        if (allowOutsideProjectRoot)
            return candidate;

        string root = Path.GetFullPath(projectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            throw Invalid("PowerForge manifest artifact path resolves outside the repository.");
        return candidate;
    }

    private static void RequireManifestPathMatch(
        string projectRoot,
        string artifactPath,
        string manifestArchive,
        string manifestExecutable,
        bool allowOutsideProjectRoot)
    {
        var candidates = new[] { manifestArchive, manifestExecutable }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolveManifestPath(projectRoot, value, allowOutsideProjectRoot));
        if (!candidates.Any(candidate => PathsEqual(candidate, artifactPath)))
            throw Invalid("Requested portable artifact is not the archive or executable recorded by the selected manifest entry.");
    }

    private static void EnsurePathWithinDirectory(string directory, string path, string label)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(path);
        string prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"{label} resolves outside the selected portable output directory.");
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsZipArchive(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(path), ".nupkg", StringComparison.OrdinalIgnoreCase);

    private static void ValidateRevision(string actual, string expected)
    {
        bool abbreviated = expected.Length < 40;
        bool matches = abbreviated
            ? actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
            : actual.Length == expected.Length && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        if (!matches)
            throw Invalid("PowerForge source provenance does not match the release workflow commit.");
    }

    private static string RequireExpectedRevision(string? value)
    {
        string revision = DotNetPublishReleaseArtifactVerifier.RequireText(value, "expected source revision");
        if (revision.Length < 7 || revision.Length > 64 || revision.Any(character => !Uri.IsHexDigit(character)))
            throw Invalid("Expected source revision must be a hexadecimal Git object id with at least seven characters.");
        return revision;
    }

    private static string NormalizeVersion(string? value)
    {
        string version = DotNetPublishReleaseArtifactVerifier.RequireText(value, "artifact version");
        if (!Version.TryParse(version, out Version? parsed))
            throw Invalid("Release artifact version must be numeric.");
        if (parsed.Revision == 0)
            return new Version(parsed.Major, parsed.Minor, parsed.Build).ToString();
        return parsed.ToString();
    }

    private static void ValidateExpectedVersion(string? expected, string actual)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(NormalizeVersion(expected), actual, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Release artifact version '{actual}' does not match expected version '{expected!.Trim()}'.");
    }

    private static string ReadPortableVersion(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        string productVersion = (version.ProductVersion ?? string.Empty).Split('+')[0].Trim();
        return Version.TryParse(productVersion, out _)
            ? productVersion
            : version.FileVersion ?? string.Empty;
    }

    private static void ValidateSbom(string path)
    {
        using JsonDocument document = ReadJson(path, "SBOM");
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
            if (!supportedVersion || version < 1 ||
                !validSerialNumber ||
                (!TryGet(root, "components", out JsonElement components) || components.ValueKind != JsonValueKind.Array) &&
                (!TryGet(root, "metadata", out JsonElement metadata) || metadata.ValueKind != JsonValueKind.Object))
            {
                throw Invalid($"CycloneDX SBOM '{Path.GetFileName(path)}' is missing supported document-level fields.");
            }
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
            {
                throw Invalid($"SPDX SBOM '{Path.GetFileName(path)}' is missing supported document-level fields.");
            }
            return;
        }

        throw Invalid($"SBOM '{Path.GetFileName(path)}' is not a recognizable CycloneDX or SPDX JSON document.");
    }

    private static JsonDocument ReadJson(string path, string label)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException exception)
        {
            throw Invalid($"{label} is not valid JSON: {exception.Message}");
        }
    }

    private static string RequireJsonText(JsonElement element, string name, string label)
    {
        string value = ReadString(element, name).Trim();
        return value.Length > 0 ? value : throw Invalid($"{label} is missing '{name}'.");
    }

    private static JsonElement[] FilterEntries(JsonElement[] entries, string propertyName, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return entries;
        string expected = selector!.Trim();
        return entries.Where(entry => Is(entry, propertyName, expected)).ToArray();
    }

    private static bool Is(JsonElement element, string name, string expected) =>
        string.Equals(ReadString(element, name), expected, StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string name) =>
        TryGet(element, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt32(JsonElement element, string name) =>
        TryGet(element, name, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static string RequireDirectory(string? path, string name)
    {
        string fullPath = Path.GetFullPath(DotNetPublishReleaseArtifactVerifier.RequireText(path, name));
        return Directory.Exists(fullPath)
            ? fullPath
            : throw new DirectoryNotFoundException($"Directory was not found: {fullPath}");
    }

    private static string ResolveRequestFile(string projectRoot, string? path, string name)
    {
        string configured = DotNetPublishReleaseArtifactVerifier.RequireText(path, name);
        string fullPath = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(projectRoot, configured));
        return File.Exists(fullPath)
            ? fullPath
            : throw new FileNotFoundException($"File was not found: {fullPath}", fullPath);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort after signature inspection closes all handles.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort after signature inspection closes all handles.
        }
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private sealed class ExpectedPortable
    {
        internal ExpectedPortable(
            DotNetPublishSpec configuration,
            DotNetPublishTarget target,
            string[] configurationPaths,
            string? signerThumbprint,
            string? signerSubjectName,
            bool allowOutsideProjectRoot)
        {
            Configuration = configuration;
            Target = target;
            ConfigurationPaths = configurationPaths;
            SignerThumbprint = signerThumbprint;
            SignerSubjectName = signerSubjectName;
            AllowOutsideProjectRoot = allowOutsideProjectRoot;
        }

        internal DotNetPublishSpec Configuration { get; }
        internal DotNetPublishTarget Target { get; }
        internal string[] ConfigurationPaths { get; }
        internal string? SignerThumbprint { get; }
        internal string? SignerSubjectName { get; }
        internal bool AllowOutsideProjectRoot { get; }
    }

    private sealed class VerifiedSignature
    {
        internal VerifiedSignature(string physicalPath, string displayPath, string subject, string thumbprint)
        {
            PhysicalPath = physicalPath;
            DisplayPath = displayPath;
            Subject = subject;
            Thumbprint = thumbprint;
        }

        internal string PhysicalPath { get; }
        internal string DisplayPath { get; }
        internal string Subject { get; }
        internal string Thumbprint { get; }
    }
}
