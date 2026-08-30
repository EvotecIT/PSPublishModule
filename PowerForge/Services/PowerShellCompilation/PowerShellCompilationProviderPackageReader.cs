using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace PowerForge;

/// <summary>
/// Reads and locks explicitly selected PowerForge provider packages without loading or executing their assemblies.
/// </summary>
public sealed class PowerShellCompilationProviderPackageReader
{
    /// <summary>Canonical package-relative provider manifest path.</summary>
    public const string ManifestPath = "powerforge/provider.json";

    /// <summary>Creates exact managed-assembly evidence without loading or executing the assembly.</summary>
    public static PowerShellCompilationProviderAssembly InspectAssembly(string assemblyPath, string packageRelativePath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath)) throw new ArgumentException("An assembly path is required.", nameof(assemblyPath));
        if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Provider assembly was not found.", assemblyPath);
        var normalizedPackagePath = NormalizePath(packageRelativePath);
        if (normalizedPackagePath.Length == 0 || normalizedPackagePath.Contains("../", StringComparison.Ordinal))
            throw new ArgumentException("A safe package-relative assembly path is required.", nameof(packageRelativePath));
        var bytes = File.ReadAllBytes(assemblyPath);
        var identity = ReadAssemblyIdentity(bytes, normalizedPackagePath);
        identity.Sha256 = Hash(bytes);
        identity.Path = normalizedPackagePath;
        return identity;
    }

    /// <summary>Resolves explicit provider packages and applies ABI, integrity, and allow/deny policy.</summary>
    public PowerShellCompilationProviderResolution Resolve(
        IEnumerable<PowerShellCompilationProviderPackageReference> packageReferences,
        PowerShellCompilationProviderTrustPolicy? trustPolicy = null)
    {
        if (packageReferences is null) throw new ArgumentNullException(nameof(packageReferences));
        var references = packageReferences
            .Where(static reference => reference is not null)
            .OrderBy(static reference => reference.Path, PathComparer())
            .ToArray();
        var duplicate = references.GroupBy(static reference => reference.Path, PathComparer()).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Provider package '{duplicate.Key}' was selected more than once.");

        var policy = trustPolicy ?? new PowerShellCompilationProviderTrustPolicy();
        var packages = new List<PowerShellCompilationProviderPackageLockEntry>();
        var providers = new List<PowerShellCompilationCommandProviderContract>();
        foreach (var reference in references)
        {
            var package = Read(reference.Path, policy);
            packages.Add(package.Lock);
            providers.AddRange(package.Providers);
        }

        EnsureUniqueProviders(providers);
        var providerLock = new PowerShellCompilationProviderLock
        {
            Packages = packages
                .OrderBy(static package => package.PackageId, StringComparer.Ordinal)
                .ThenBy(static package => package.PackageVersion, StringComparer.Ordinal)
                .ToArray()
        };
        providerLock.LockSha256 = ComputeLockSha256(providerLock);
        return new PowerShellCompilationProviderResolution
        {
            Providers = providers.OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal).ToArray(),
            Lock = providerLock
        };
    }

    /// <summary>Computes the canonical lock SHA-256 and ignores the lock's current hash field.</summary>
    public static string ComputeLockSha256(PowerShellCompilationProviderLock providerLock)
    {
        if (providerLock is null) throw new ArgumentNullException(nameof(providerLock));
        var canonical = new
        {
            providerLock.SchemaVersion,
            Packages = (providerLock.Packages ?? Array.Empty<PowerShellCompilationProviderPackageLockEntry>())
                .OrderBy(static package => package.PackageId, StringComparer.Ordinal)
                .ThenBy(static package => package.PackageVersion, StringComparer.Ordinal)
                .Select(static package => new
                {
                    package.PackageId,
                    package.PackageVersion,
                    package.ProviderAbiVersion,
                    package.PackageSha256,
                    package.ManifestSha256,
                    package.Signature,
                    package.Publisher,
                    package.LicenseExpression,
                    Assemblies = (package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
                        .OrderBy(static assembly => assembly.Path, StringComparer.Ordinal)
                        .Select(static assembly => new { assembly.Path, assembly.Sha256, assembly.AssemblyName, assembly.AssemblyVersion, assembly.PublicKeyToken }),
                    Dependencies = (package.Dependencies ?? Array.Empty<PowerShellCompilationProviderDependency>())
                        .OrderBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
                        .Select(static dependency => new { dependency.PackageId, dependency.Version, dependency.ContentHash }),
                    ProviderIds = (package.ProviderIds ?? Array.Empty<string>()).OrderBy(static id => id, StringComparer.Ordinal)
                })
        };
        return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
    }

    /// <summary>Fails unless the actual provider lock exactly matches a separately reviewed lock.</summary>
    public static void EnsureMatches(
        PowerShellCompilationProviderLock expected,
        PowerShellCompilationProviderLock actual)
    {
        if (expected is null) throw new ArgumentNullException(nameof(expected));
        if (actual is null) throw new ArgumentNullException(nameof(actual));
        var expectedHash = ComputeLockSha256(expected);
        if (!string.Equals(expectedHash, expected.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Expected provider lock content does not match its SHA-256.");
        var actualHash = ComputeLockSha256(actual);
        if (!string.Equals(actualHash, actual.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Actual provider lock content does not match its SHA-256.");
        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Provider lock mismatch. Expected '{expectedHash}', actual '{actualHash}'.");
    }

    private static ProviderPackage Read(string path, PowerShellCompilationProviderTrustPolicy policy)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("PowerForge provider package was not found.", path);
        var packageBytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var packageReader = new PackageArchiveReader(stream, leaveStreamOpen: true);
        var signature = packageReader.GetPrimarySignatureAsync(CancellationToken.None).GetAwaiter().GetResult();
        var signatureState = "Unsigned";
        if (signature is not null)
        {
            packageReader.ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None).GetAwaiter().GetResult();
            signatureState = "ValidIntegrity";
        }
        if (policy.RequirePackageSignature && signature is null)
            throw new InvalidOperationException($"Provider package '{path}' is unsigned, but policy requires a NuGet package signature.");

        var files = packageReader.GetFiles().Select(NormalizePath).ToArray();
        if (files.Count(file => string.Equals(file, ManifestPath, StringComparison.Ordinal)) != 1)
            throw new InvalidOperationException($"Provider package '{path}' must contain exactly one case-exact '{ManifestPath}'.");
        if (files.GroupBy(static file => file, StringComparer.Ordinal).Any(static group => group.Count() > 1))
            throw new InvalidOperationException($"Provider package '{path}' contains duplicate case-exact archive entries.");

        byte[] manifestBytes;
        using (var manifestStream = packageReader.GetStream(ManifestPath))
        using (var memory = new MemoryStream())
        {
            manifestStream.CopyTo(memory);
            manifestBytes = memory.ToArray();
        }
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
        options.Converters.Add(new JsonStringEnumConverter());
        var manifest = JsonSerializer.Deserialize<PowerShellCompilationProviderPackageManifest>(manifestBytes, options)
            ?? throw new InvalidOperationException($"Provider package '{path}' contains an empty manifest.");
        ValidateManifest(manifest, policy, path);
        var assemblies = ValidateAssemblies(packageReader, files, manifest, path);

        return new ProviderPackage(
            manifest.Providers,
            new PowerShellCompilationProviderPackageLockEntry
            {
                PackageId = manifest.PackageId,
                PackageVersion = manifest.PackageVersion,
                ProviderAbiVersion = manifest.ProviderAbiVersion,
                PackageSha256 = Hash(packageBytes),
                ManifestSha256 = Hash(manifestBytes),
                Signature = signatureState,
                Publisher = manifest.Publisher,
                LicenseExpression = manifest.LicenseExpression,
                Assemblies = assemblies,
                Dependencies = manifest.Dependencies
                    .OrderBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
                    .ToArray(),
                ProviderIds = manifest.Providers
                    .Select(static provider => provider.ProviderId)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray()
            });
    }

    private static void ValidateManifest(
        PowerShellCompilationProviderPackageManifest manifest,
        PowerShellCompilationProviderTrustPolicy policy,
        string path)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidOperationException($"Provider package '{path}' uses unsupported manifest schema '{manifest.SchemaVersion}'.");
        if (!string.Equals(manifest.ProviderAbiVersion, PowerShellCompilationProviderAbi.CurrentVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Provider package '{path}' targets ABI '{manifest.ProviderAbiVersion}', expected '{PowerShellCompilationProviderAbi.CurrentVersion}'.");
        Require(manifest.PackageId, "PackageId", path);
        Require(manifest.PackageVersion, "PackageVersion", path);
        Require(manifest.Publisher, "Publisher", path);
        Require(manifest.LicenseExpression, "LicenseExpression", path);
        if (!Version.TryParse(manifest.PackageVersion, out var packageVersion) || packageVersion.Build < 0 || packageVersion.Revision >= 0)
            throw new InvalidOperationException($"Provider package '{path}' must use a three-part public PackageVersion.");
        ApplyIdentityPolicy(manifest, policy, path);
        if (manifest.SemanticProfiles.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one semantic profile.");
        if (manifest.Assemblies.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one provider assembly.");
        if (manifest.Providers.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one provider contract.");

        foreach (var dependency in manifest.Dependencies)
        {
            Require(dependency.PackageId, "Dependency.PackageId", path);
            Require(dependency.Version, "Dependency.Version", path);
            Require(dependency.ContentHash, "Dependency.ContentHash", path);
            if (!Version.TryParse(dependency.Version, out var dependencyVersion) || dependencyVersion.Build < 0 || dependencyVersion.Revision >= 0)
                throw new InvalidOperationException($"Provider package '{path}' dependency '{dependency.PackageId}' must use an exact three-part public version.");
        }
        foreach (var provider in manifest.Providers)
        {
            Require(provider.ProviderId, "Provider.ProviderId", path);
            Require(provider.ProviderVersion, "Provider.ProviderVersion", path);
            Require(provider.FeatureId, "Provider.FeatureId", path);
            Require(provider.CommandName, "Provider.CommandName", path);
            if (!provider.CompileTimeOnly || provider.MayExecuteSource || provider.MayImportSourceModules)
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' must be compile-time-only and may not execute or import source.");
            if (provider.Adapter is null || string.IsNullOrWhiteSpace(provider.Adapter.SemanticProfile))
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' must declare an adapter semantic profile.");
            if (!manifest.SemanticProfiles.Contains(provider.Adapter.SemanticProfile, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' targets undeclared semantic profile '{provider.Adapter.SemanticProfile}'.");
            ApplyProviderPolicy(provider.ProviderId, policy, path);
        }
        EnsureUniqueProviders(manifest.Providers);
    }

    private static PowerShellCompilationProviderAssembly[] ValidateAssemblies(
        PackageArchiveReader packageReader,
        IReadOnlyCollection<string> files,
        PowerShellCompilationProviderPackageManifest manifest,
        string packagePath)
    {
        var result = new List<PowerShellCompilationProviderAssembly>();
        foreach (var declared in manifest.Assemblies.OrderBy(static assembly => assembly.Path, StringComparer.Ordinal))
        {
            var path = NormalizePath(declared.Path);
            if (path.Length == 0 || path.StartsWith("/", StringComparison.Ordinal) || path.Contains("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Provider package '{packagePath}' contains unsafe assembly path '{declared.Path}'.");
            if (!files.Contains(path, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider package '{packagePath}' is missing declared assembly '{path}'.");
            byte[] bytes;
            using (var source = packageReader.GetStream(path))
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                bytes = memory.ToArray();
            }
            var sha256 = Hash(bytes);
            if (!string.Equals(sha256, declared.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Provider assembly '{path}' SHA-256 does not match its manifest.");
            var identity = ReadAssemblyIdentity(bytes, path);
            if (!string.Equals(identity.AssemblyName, declared.AssemblyName, StringComparison.Ordinal) ||
                !string.Equals(identity.AssemblyVersion, declared.AssemblyVersion, StringComparison.Ordinal) ||
                !string.Equals(identity.PublicKeyToken, declared.PublicKeyToken, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Provider assembly '{path}' managed identity does not match its manifest.");
            result.Add(new PowerShellCompilationProviderAssembly
            {
                Path = path,
                Sha256 = sha256,
                AssemblyName = identity.AssemblyName,
                AssemblyVersion = identity.AssemblyVersion,
                PublicKeyToken = identity.PublicKeyToken
            });
        }
        return result.ToArray();
    }

    private static PowerShellCompilationProviderAssembly ReadAssemblyIdentity(byte[] bytes, string path)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata) throw new InvalidOperationException($"Provider assembly '{path}' has no managed metadata.");
        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly) throw new InvalidOperationException($"Provider assembly '{path}' is not a managed assembly.");
        var definition = reader.GetAssemblyDefinition();
        var publicKey = reader.GetBlobBytes(definition.PublicKey);
        return new PowerShellCompilationProviderAssembly
        {
            Path = path,
            AssemblyName = reader.GetString(definition.Name),
            AssemblyVersion = definition.Version.ToString(),
            PublicKeyToken = ComputePublicKeyToken(publicKey)
        };
    }

    private static string ComputePublicKeyToken(byte[] publicKey)
    {
        if (publicKey.Length == 0) return string.Empty;
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(publicKey);
        return string.Concat(hash.Skip(hash.Length - 8).Reverse().Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static void ApplyIdentityPolicy(
        PowerShellCompilationProviderPackageManifest manifest,
        PowerShellCompilationProviderTrustPolicy policy,
        string path)
    {
        if (Contains(policy.DeniedPackageIds, manifest.PackageId))
            throw new InvalidOperationException($"Provider package '{manifest.PackageId}' is denied by policy.");
        if (policy.AllowedPackageIds.Length > 0 && !Contains(policy.AllowedPackageIds, manifest.PackageId))
            throw new InvalidOperationException($"Provider package '{manifest.PackageId}' is not allowed by policy.");
        if (policy.AllowedPublishers.Length > 0 && !Contains(policy.AllowedPublishers, manifest.Publisher))
            throw new InvalidOperationException($"Provider package '{path}' publisher '{manifest.Publisher}' is not allowed by policy.");
        if (policy.AllowedLicenseExpressions.Length > 0 && !Contains(policy.AllowedLicenseExpressions, manifest.LicenseExpression))
            throw new InvalidOperationException($"Provider package '{path}' license '{manifest.LicenseExpression}' is not allowed by policy.");
    }

    private static void ApplyProviderPolicy(string providerId, PowerShellCompilationProviderTrustPolicy policy, string path)
    {
        if (Contains(policy.DeniedProviderIds, providerId))
            throw new InvalidOperationException($"Provider '{providerId}' from package '{path}' is denied by policy.");
        if (policy.AllowedProviderIds.Length > 0 && !Contains(policy.AllowedProviderIds, providerId))
            throw new InvalidOperationException($"Provider '{providerId}' from package '{path}' is not allowed by policy.");
    }

    private static bool Contains(IEnumerable<string>? values, string value)
        => (values ?? Array.Empty<string>()).Any(candidate => string.Equals(candidate?.Trim(), value, StringComparison.OrdinalIgnoreCase));

    private static void EnsureUniqueProviders(IEnumerable<PowerShellCompilationCommandProviderContract> providers)
    {
        var duplicate = providers.GroupBy(static provider => provider.ProviderId, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Provider identity '{duplicate.Key}' is declared more than once.");
    }

    private static void Require(string value, string property, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Provider package '{path}' requires non-empty {property}.");
    }

    private static string NormalizePath(string value) => (value ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static StringComparer PathComparer() => Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static string Hash(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        return string.Concat(sha256.ComputeHash(bytes).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private sealed class ProviderPackage
    {
        internal ProviderPackage(PowerShellCompilationCommandProviderContract[] providers, PowerShellCompilationProviderPackageLockEntry providerLock)
        {
            Providers = providers;
            Lock = providerLock;
        }

        internal PowerShellCompilationCommandProviderContract[] Providers { get; }
        internal PowerShellCompilationProviderPackageLockEntry Lock { get; }
    }
}
