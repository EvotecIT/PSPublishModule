using System.Diagnostics;
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
    private readonly Func<string, string> _readPortableIdentity;
    private readonly Func<byte[], byte[], PowerForgePayloadInventorySignature> _verifyPortableInventory;

    /// <summary>Creates a verifier backed by WinTrust and signed-file version metadata.</summary>
    public PowerForgeReleaseArtifactVerifier()
        : this(
            CreateDefaultAuthenticodeVerifier(),
            ReadPortableVersion,
            ReadPortableIdentity,
            PowerForgePortablePayloadInventoryCms.Verify)
    {
    }

    internal PowerForgeReleaseArtifactVerifier(
        Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult> verifyAuthenticode,
        Func<string, string> readPortableVersion,
        Func<string, string>? readPortableIdentity = null,
        Func<byte[], byte[], PowerForgePayloadInventorySignature>? verifyPortableInventory = null)
    {
        _verifyAuthenticode = verifyAuthenticode ?? throw new ArgumentNullException(nameof(verifyAuthenticode));
        _readPortableVersion = readPortableVersion ?? throw new ArgumentNullException(nameof(readPortableVersion));
        _readPortableIdentity = readPortableIdentity ?? ReadPortableIdentity;
        _verifyPortableInventory = verifyPortableInventory ?? PowerForgePortablePayloadInventoryCms.Verify;
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
        string? manifestPath,
        IEnumerable<string>? configurationPaths,
        IEnumerable<string>? sbomPaths,
        string artifactId,
        string artifactVersion,
        string artifactDigest)
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
        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            evidence.Add(new PowerForgeReleaseEvidenceFile
            {
                Role = "manifest",
                Path = manifestPath!,
                Sha256 = DotNetPublishReleaseArtifactVerifier.ComputeSha256(manifestPath!)
            });
        }
        foreach (string configurationPath in configurationPaths ?? Array.Empty<string>())
        {
            string digest = VerifyChecksummedFile(
                projectRoot,
                checksumsPath,
                configurationPath,
                "PowerForge configuration");
            evidence.Add(new PowerForgeReleaseEvidenceFile
            {
                Role = "configuration",
                Path = configurationPath!,
                Sha256 = digest
            });
        }

        foreach (string configuredPath in sbomPaths ?? Array.Empty<string>())
        {
            string path = ResolveRequestFile(projectRoot, configuredPath, "SBOM path");
            string digest = VerifyChecksummedFile(projectRoot, checksumsPath, path, "SBOM");
            ValidateSbom(path, artifactId, artifactVersion, artifactDigest);
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
            !DotNetPublishReleaseArtifactVerifier.CertificateSubjectsEqual(signature.Subject, expectedSubject))
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
        if (actual.Length != expected.Length ||
            !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw Invalid("PowerForge source provenance does not match the release workflow commit.");
    }

    private static string RequireExpectedRevision(string? value)
    {
        return DotNetPublishReleaseArtifactVerifier.RequireFullGitObjectId(
            value,
            "expected source revision");
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

    private static string NormalizePortableVersion(string? value)
    {
        string text = DotNetPublishReleaseArtifactVerifier.RequireText(value, "artifact version");
        int metadataSeparator = text.IndexOf('+');
        if (metadataSeparator >= 0)
            text = text.Substring(0, metadataSeparator);
        int prereleaseSeparator = text.IndexOf('-');
        return prereleaseSeparator < 0
            ? NormalizeVersion(text)
            : NormalizeModuleVersion(text.Substring(0, prereleaseSeparator), text.Substring(prereleaseSeparator + 1));
    }

    private static void ValidateExpectedPortableVersion(string? expected, string actual)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(NormalizePortableVersion(expected), actual, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Release artifact version '{actual}' does not match expected version '{expected!.Trim()}'.");
    }

    private static string ReadPortableVersion(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        string productVersion = (version.ProductVersion ?? string.Empty).Trim();
        if (productVersion.Length > 0)
        {
            try
            {
                _ = NormalizePortableVersion(productVersion);
                return productVersion;
            }
            catch (InvalidDataException)
            {
                // Windows resources can carry descriptive ProductVersion text. Preserve any signed source object ID
                // while using the numeric FileVersion for the semantic artifact identity.
                string fileVersion = version.FileVersion ?? string.Empty;
                string? sourceRevision = System.Text.RegularExpressions.Regex.Matches(
                        productVersion,
                        @"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{64}|[0-9A-Fa-f]{40})(?![0-9A-Fa-f])")
                    .Cast<System.Text.RegularExpressions.Match>()
                    .Select(match => match.Value)
                    .FirstOrDefault();
                return sourceRevision is null ? fileVersion : fileVersion + "+" + sourceRevision;
            }
        }
        return version.FileVersion ?? string.Empty;
    }

    private static string ReadPortableIdentity(string path)
    {
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(path);
        return new[] { version.ProductName, version.InternalName, version.OriginalFilename, Path.GetFileNameWithoutExtension(path) }
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim()
            ?? throw Invalid("Signed portable executable identity metadata is missing.");
    }

    private static string NormalizeModuleVersion(string version, string? prerelease = null)
    {
        string numeric = NormalizeVersion(version);
        string label = (prerelease ?? string.Empty).Trim();
        if (label.Length == 0)
            return numeric;
        if (label.StartsWith("-", StringComparison.Ordinal))
            label = label.Substring(1);
        if (label.Length == 0 || label.Any(character =>
                !(char.IsLetterOrDigit(character) || character == '.' || character == '-')))
            throw Invalid("PowerShell module prerelease label is not valid.");
        return numeric + "-" + label;
    }

    private static void ValidateExpectedModuleVersion(string? expected, string actual)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(NormalizeModuleVersionText(expected!), actual, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Release artifact version '{actual}' does not match expected version '{expected!.Trim()}'.");
    }

    private static string NormalizeModuleVersionText(string value)
    {
        string text = DotNetPublishReleaseArtifactVerifier.RequireText(value, "artifact version");
        int separator = text.IndexOf('-');
        return separator < 0
            ? NormalizeModuleVersion(text)
            : NormalizeModuleVersion(text.Substring(0, separator), text.Substring(separator + 1));
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

    private static string[] ReadStringArray(JsonElement element, string name)
    {
        if (!TryGet(element, name, out JsonElement value))
            return Array.Empty<string>();
        if (value.ValueKind != JsonValueKind.Array)
            throw Invalid($"PowerForge manifest property '{name}' must be an array.");
        return value.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? string.Empty
                : throw Invalid($"PowerForge manifest property '{name}' must contain only strings.")).ToArray();
    }

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
            string[] executableIdentities,
            DotNetPublishSignOptions sign,
            string? signerThumbprint,
            string? signerSubjectName,
            bool allowOutsideProjectRoot)
        {
            Configuration = configuration;
            Target = target;
            ConfigurationPaths = configurationPaths;
            ExecutableIdentities = executableIdentities;
            Sign = sign;
            SignerThumbprint = signerThumbprint;
            SignerSubjectName = signerSubjectName;
            AllowOutsideProjectRoot = allowOutsideProjectRoot;
        }

        internal DotNetPublishSpec Configuration { get; }
        internal DotNetPublishTarget Target { get; }
        internal string[] ConfigurationPaths { get; }
        internal string[] ExecutableIdentities { get; }
        internal DotNetPublishSignOptions Sign { get; }
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
