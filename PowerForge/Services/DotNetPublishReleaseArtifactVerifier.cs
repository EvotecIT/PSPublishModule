using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>
/// Verifies that a signed MSI still matches the PowerForge build manifest, checksum catalog,
/// source provenance, package identity, and signing configuration that produced it.
/// </summary>
public sealed class DotNetPublishReleaseArtifactVerifier
{
    private static readonly JsonSerializerOptions ConfigurationJsonOptions = CreateConfigurationJsonOptions();
    private readonly Func<string, DotNetPublishMsiPackageMetadata> _readPackage;
    private readonly Func<string, AuthenticodeResult> _verifyAuthenticode;

    /// <summary>Creates a verifier backed by Windows Installer and WinTrust.</summary>
    public DotNetPublishReleaseArtifactVerifier()
        : this(
            path => new MsiPackageMetadataReader().Read(path),
            VerifyAuthenticode)
    {
    }

    internal DotNetPublishReleaseArtifactVerifier(
        Func<string, DotNetPublishMsiPackageMetadata> readPackage,
        Func<string, AuthenticodeResult> verifyAuthenticode)
    {
        _readPackage = readPackage ?? throw new ArgumentNullException(nameof(readPackage));
        _verifyAuthenticode = verifyAuthenticode ?? throw new ArgumentNullException(nameof(verifyAuthenticode));
    }

    /// <summary>Verifies one configured installer and returns trusted local release metadata.</summary>
    public DotNetPublishReleaseArtifact Verify(DotNetPublishReleaseArtifactVerificationRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        var projectRoot = RequireDirectory(request.ProjectRoot, nameof(request.ProjectRoot));
        var manifestPath = RequireFile(request.ManifestPath, nameof(request.ManifestPath));
        var checksumsPath = RequireFile(request.ChecksumsPath, nameof(request.ChecksumsPath));
        var configurationPath = RequireFile(request.ConfigurationPath, nameof(request.ConfigurationPath));
        var installerId = RequireText(request.InstallerId, nameof(request.InstallerId));

        var manifestRelativePath = GetRelativePath(projectRoot, manifestPath).Replace('\\', '/');
        var manifestDigest = ComputeSha256(manifestPath);
        if (!ChecksumContains(checksumsPath, manifestRelativePath, manifestDigest))
            throw Invalid("PowerForge manifest SHA-256 does not match the checksum manifest.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (manifest.RootElement.ValueKind != JsonValueKind.Array)
            throw Invalid("PowerForge manifest must contain a JSON array.");

        var entries = manifest.RootElement.EnumerateArray()
            .Where(entry => Is(entry, "Category", "Installer") && Is(entry, "InstallerId", installerId))
            .ToArray();
        entries = FilterEntries(entries, "Target", request.Target);
        entries = FilterEntries(entries, "Runtime", request.Runtime);
        entries = FilterEntries(entries, "Framework", request.Framework);
        entries = FilterEntries(entries, "Style", request.Style);
        if (entries.Length != 1)
            throw Invalid(
                $"PowerForge manifest selectors must identify exactly one '{installerId}' installer; " +
                "specify target, RID, framework, and style for matrix builds.");

        var entry = entries[0];
        if (ReadInt32(entry, "SignedFiles") < 1)
            throw Invalid("PowerForge manifest does not attest that the installer was signed.");
        if (!TryGet(entry, "SourceDirty", out var sourceDirty) || sourceDirty.ValueKind != JsonValueKind.False)
            throw Invalid("PowerForge manifest must come from a clean source checkout.");

        var sourceRevision = RequireHex(ReadString(entry, "SourceRevision"), 7, 64, "source revision");
        var expectedRevision = RequireHex(request.ExpectedSourceRevision, 7, 64, "expected source revision");
        if (!string.Equals(sourceRevision, expectedRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("PowerForge manifest source revision does not match the release workflow commit.");
        }

        var outputFiles = ReadStringArray(entry, "OutputFiles");
        var packageMetadata = ReadArray(entry, "PackageMetadata");
        if (outputFiles.Length != 1 || packageMetadata.Length != 1)
            throw Invalid("PowerForge installer entry must contain exactly one output and one package metadata record.");

        var relativePath = NormalizeRelativePath(outputFiles[0]);
        var artifactPath = ResolveWithinRoot(projectRoot, relativePath);
        if (!File.Exists(artifactPath))
            throw new FileNotFoundException("PowerForge installer output was not found.", artifactPath);

        var manifestPackage = packageMetadata[0];
        if (!string.Equals(
                NormalizeRelativePath(ReadString(manifestPackage, "Path")),
                relativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("PowerForge package metadata does not describe the installer output.");
        }
        if (!string.IsNullOrWhiteSpace(ReadString(manifestPackage, "ReadError")))
            throw Invalid("PowerForge manifest reports that MSI package metadata could not be read.");

        var expected = ReadExpectedInstaller(configurationPath, installerId);
        var actual = _readPackage(artifactPath);
        ValidatePackage(manifestPackage, actual, expected);

        var digest = ComputeSha256(artifactPath);
        if (!ChecksumContains(checksumsPath, relativePath, digest))
            throw Invalid("Installer SHA-256 does not match the PowerForge checksum manifest.");

        var signature = _verifyAuthenticode(artifactPath);
        if (!signature.IsValid)
            throw Invalid($"Installer Authenticode signature is not valid (0x{signature.StatusCode:X8}).");
        if (!string.Equals(signature.Thumbprint, expected.SignerThumbprint, StringComparison.OrdinalIgnoreCase))
            throw Invalid("Installer signature does not use the configured release certificate.");

        return new DotNetPublishReleaseArtifact
        {
            InstallerId = installerId,
            ArtifactPath = artifactPath,
            FileName = Path.GetFileName(artifactPath),
            Sha256 = digest,
            Version = NormalizeVersion(actual.ProductVersion),
            ProductCode = NormalizeGuid(actual.ProductCode, "ProductCode"),
            UpgradeCode = NormalizeGuid(actual.UpgradeCode, "UpgradeCode"),
            ProductName = RequireText(actual.ProductName, "ProductName"),
            Manufacturer = RequireText(actual.Manufacturer, "Manufacturer"),
            SourceRevision = sourceRevision.ToLowerInvariant(),
            SignerSubject = signature.Subject,
            SignerThumbprint = signature.Thumbprint
        };
    }

    private static ExpectedInstaller ReadExpectedInstaller(string configurationPath, string installerId)
    {
        var configuration = JsonSerializer.Deserialize<DotNetPublishSpec>(
                File.ReadAllText(configurationPath),
                ConfigurationJsonOptions)
            ?? throw Invalid("PowerForge configuration could not be deserialized.");
        configuration = DotNetPublishPipelineRunner.ResolveProfile(configuration);
        var installers = (configuration.Installers ?? Array.Empty<DotNetPublishInstaller>())
            .Where(installer => string.Equals(installer.Id, installerId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (installers.Length != 1)
            throw Invalid($"PowerForge configuration must define exactly one '{installerId}' installer.");

        var installer = installers[0];
        var sign = DotNetPublishSigningProfileResolver.ResolveConfiguredSignOptions(
            configuration.SigningProfiles,
            installer.SignProfile,
            installer.Sign,
            installer.SignOverrides,
            $"Installer '{installerId}'");
        if (sign is null || !sign.Enabled)
            throw Invalid("PowerForge installer signing must be enabled for a release artifact.");

        if (installer.Authoring is null &&
            string.IsNullOrWhiteSpace(installer.InstallerProjectId) &&
            string.IsNullOrWhiteSpace(installer.InstallerProjectPath))
        {
            throw Invalid("PowerForge installer authoring or a hand-authored installer project is required for release verification.");
        }

        var product = installer.Authoring?.Product;

        return new ExpectedInstaller(
            product is null ? null : RequireText(product.Name, "Product.Name"),
            product is null ? null : RequireText(product.Manufacturer, "Product.Manufacturer"),
            product is null ? null : NormalizeGuid(product.UpgradeCode, "Product.UpgradeCode"),
            NormalizeThumbprint(sign.Thumbprint));
    }

    private static void ValidatePackage(
        JsonElement manifestPackage,
        DotNetPublishMsiPackageMetadata actual,
        ExpectedInstaller expected)
    {
        ValidateEqual(ReadString(manifestPackage, "ProductName"), actual.ProductName, "ProductName");
        ValidateEqual(ReadString(manifestPackage, "Manufacturer"), actual.Manufacturer, "Manufacturer");
        ValidateEqual(ReadString(manifestPackage, "ProductVersion"), actual.ProductVersion, "ProductVersion");
        ValidateEqual(NormalizeGuid(ReadString(manifestPackage, "ProductCode"), "ProductCode"), NormalizeGuid(actual.ProductCode, "ProductCode"), "ProductCode");
        ValidateEqual(NormalizeGuid(ReadString(manifestPackage, "UpgradeCode"), "UpgradeCode"), NormalizeGuid(actual.UpgradeCode, "UpgradeCode"), "UpgradeCode");
        if (expected.ProductName is not null)
            ValidateEqual(expected.ProductName, actual.ProductName, "configured ProductName");
        if (expected.Manufacturer is not null)
            ValidateEqual(expected.Manufacturer, actual.Manufacturer, "configured Manufacturer");
        if (expected.UpgradeCode is not null)
            ValidateEqual(expected.UpgradeCode, NormalizeGuid(actual.UpgradeCode, "UpgradeCode"), "configured UpgradeCode");
        _ = NormalizeVersion(actual.ProductVersion);
    }

    private static void ValidateEqual(string? expected, string? actual, string name)
    {
        if (!string.Equals(expected?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw Invalid($"PowerForge manifest or configuration does not match MSI {name}.");
    }

    private static AuthenticodeResult VerifyAuthenticode(string path)
    {
        var status = WindowsAuthenticodeSignatureInspector.Verify(path);
        if (status != 0)
            return new AuthenticodeResult(false, status, string.Empty, string.Empty);

        try
        {
#pragma warning disable SYSLIB0057 // The certificate is embedded in a signed file, not loaded from certificate bytes.
            using var signedCertificate = X509Certificate.CreateFromSignedFile(path);
            using var certificate = new X509Certificate2(signedCertificate);
#pragma warning restore SYSLIB0057
            return new AuthenticodeResult(
                true,
                status,
                certificate.Subject,
                NormalizeThumbprint(certificate.Thumbprint));
        }
        catch (CryptographicException)
        {
            return new AuthenticodeResult(false, unchecked((int)0x800B0100), string.Empty, string.Empty);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var input = File.OpenRead(path);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
    }

    private static bool ChecksumContains(string path, string relativePath, string digest)
    {
        var expected = relativePath.Replace('\\', '/');
        foreach (var line in File.ReadLines(path))
        {
            var separator = line.IndexOf(" *", StringComparison.Ordinal);
            if (separator <= 0) continue;
            var listedDigest = line.Substring(0, separator).Trim();
            var listedPath = line.Substring(separator + 2).Trim().Replace('\\', '/');
            if (string.Equals(listedDigest, digest, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(listedPath, expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonElement[] FilterEntries(JsonElement[] entries, string propertyName, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return entries;

        var expected = selector!.Trim();
        return entries
            .Where(entry => Is(entry, propertyName, expected))
            .ToArray();
    }

    private static string GetRelativePath(string root, string path)
    {
#if NET472
        var basePath = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(Path.GetFullPath(path));
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(targetUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
#else
        return Path.GetRelativePath(root, path);
#endif
    }

    private static JsonSerializerOptions CreateConfigurationJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static string ResolveWithinRoot(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            throw Invalid("PowerForge manifest installer path must be relative to the repository.");
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw Invalid("PowerForge manifest installer path resolves outside the repository.");
        return candidate;
    }

    private static string RequireDirectory(string? path, string name)
    {
        var full = Path.GetFullPath(RequireText(path, name));
        return Directory.Exists(full) ? full : throw new DirectoryNotFoundException($"Directory was not found: {full}");
    }

    private static string RequireFile(string? path, string name)
    {
        var full = Path.GetFullPath(RequireText(path, name));
        return File.Exists(full) ? full : throw new FileNotFoundException($"File was not found: {full}", full);
    }

    private static string RequireText(string? value, string name)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length > 0 ? normalized : throw new InvalidDataException($"{name} is required.");
    }

    private static string RequireHex(string? value, int minimum, int maximum, string name)
    {
        var normalized = RequireText(value, name);
        if (normalized.Length < minimum || normalized.Length > maximum || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw Invalid($"PowerForge manifest does not contain a valid {name}.");
        return normalized;
    }

    private static string NormalizeRelativePath(string? value)
    {
        var normalized = RequireText(value, "manifest artifact path").Replace('\\', '/').TrimStart('/');
        return normalized;
    }

    private static string NormalizeVersion(string? value)
    {
        var normalized = RequireText(value, "ProductVersion");
        if (!Version.TryParse(normalized, out _))
            throw Invalid("MSI ProductVersion is not a numeric version.");
        return normalized;
    }

    private static string NormalizeGuid(string? value, string name)
    {
        if (!Guid.TryParse(value, out var parsed))
            throw Invalid($"MSI {name} is not a valid GUID.");
        return parsed.ToString("B").ToUpperInvariant();
    }

    private static string NormalizeThumbprint(string? value)
    {
        var normalized = RequireText(value, "signing certificate thumbprint")
            .Replace(" ", string.Empty)
            .ToUpperInvariant();
        if (normalized.Length < 40 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw Invalid("PowerForge signing certificate thumbprint is invalid.");
        return normalized;
    }

    private static bool Is(JsonElement element, string name, string expected) =>
        string.Equals(ReadString(element, name), expected, StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt32(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static string[] ReadStringArray(JsonElement element, string name) =>
        ReadArray(element, name)
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();

    private static JsonElement[] ReadArray(JsonElement element, string name) =>
        TryGet(element, name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToArray()
            : Array.Empty<JsonElement>();

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
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

    private static InvalidDataException Invalid(string message) => new(message);

    internal sealed class AuthenticodeResult
    {
        internal AuthenticodeResult(bool isValid, int statusCode, string subject, string thumbprint)
        {
            IsValid = isValid;
            StatusCode = statusCode;
            Subject = subject;
            Thumbprint = thumbprint;
        }

        internal bool IsValid { get; }
        internal int StatusCode { get; }
        internal string Subject { get; }
        internal string Thumbprint { get; }
    }

    private sealed class ExpectedInstaller
    {
        internal ExpectedInstaller(string? productName, string? manufacturer, string? upgradeCode, string signerThumbprint)
        {
            ProductName = productName;
            Manufacturer = manufacturer;
            UpgradeCode = upgradeCode;
            SignerThumbprint = signerThumbprint;
        }

        internal string? ProductName { get; }
        internal string? Manufacturer { get; }
        internal string? UpgradeCode { get; }
        internal string SignerThumbprint { get; }
    }
}
