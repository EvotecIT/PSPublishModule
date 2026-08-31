using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Signing;

namespace PowerForge;

/// <summary>
/// Reads and locks explicitly selected PowerForge provider packages without loading or executing their assemblies.
/// </summary>
public sealed partial class PowerShellCompilationProviderPackageReader
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
        PowerShellCompilationProviderTrustPolicy? trustPolicy = null,
        string? semanticProfileId = null,
        string? runtimeIdentifier = null)
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
        var semanticProfile = PowerShellCompilationSemanticOracleCatalog.Get(
            string.IsNullOrWhiteSpace(semanticProfileId)
                ? PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId
                : semanticProfileId!.Trim()).ProfileId;
        var packages = new List<PowerShellCompilationProviderPackageLockEntry>();
        var providers = new List<PowerShellCompilationCommandProviderContract>();
        var runtimeAssemblies = new List<PowerShellCompilationResolvedProviderAssembly>();
        var runtimeNativeAssets = new List<PowerShellCompilationResolvedProviderNativeAsset>();
        foreach (var reference in references)
        {
            var package = Read(reference.Path, policy, semanticProfile, runtimeIdentifier);
            packages.Add(package.Lock);
            providers.AddRange(package.Providers);
            runtimeAssemblies.AddRange(package.RuntimeAssemblies);
            runtimeNativeAssets.AddRange(package.RuntimeNativeAssets);
        }

        EnsureUniqueProviders(providers);
        var duplicateAssembly = runtimeAssemblies
            .GroupBy(static assembly => assembly.Assembly.AssemblyName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateAssembly is not null)
            throw new InvalidOperationException($"Provider assembly identity '{duplicateAssembly.Key}' is supplied by more than one package assembly.");
        var duplicateNativeAsset = runtimeNativeAssets
            .GroupBy(static asset => asset.Asset.FileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateNativeAsset is not null)
            throw new InvalidOperationException($"Provider native asset file name '{duplicateNativeAsset.Key}' is supplied more than once for the selected target.");
        var duplicateRuntimeFile = runtimeAssemblies
            .Select(static assembly => assembly.Assembly.AssemblyName + ".dll")
            .Concat(runtimeNativeAssets.Select(static asset => asset.Asset.FileName))
            .GroupBy(static fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateRuntimeFile is not null)
            throw new InvalidOperationException($"Provider runtime file name '{duplicateRuntimeFile.Key}' collides across the selected managed and native closure.");
        var providerLock = new PowerShellCompilationProviderLock
        {
            SemanticProfileId = semanticProfile,
            Packages = packages
                .OrderBy(static package => package.PackageId, StringComparer.Ordinal)
                .ThenBy(static package => package.PackageVersion, StringComparer.Ordinal)
                .ToArray()
        };
        providerLock.LockSha256 = ComputeLockSha256(providerLock);
        return new PowerShellCompilationProviderResolution
        {
            Providers = providers.OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal).ToArray(),
            Lock = providerLock,
            RuntimeAssemblies = runtimeAssemblies
                .OrderBy(static assembly => assembly.PackageId, StringComparer.Ordinal)
                .ThenBy(static assembly => assembly.Assembly.Path, StringComparer.Ordinal)
                .ToArray(),
            RuntimeNativeAssets = runtimeNativeAssets
                .OrderBy(static asset => asset.PackageId, StringComparer.Ordinal)
                .ThenBy(static asset => asset.Asset.Path, StringComparer.Ordinal)
                .ToArray()
        };
    }

    /// <summary>Computes the canonical lock SHA-256 and ignores the lock's current hash field.</summary>
    public static string ComputeLockSha256(PowerShellCompilationProviderLock providerLock)
    {
        if (providerLock is null) throw new ArgumentNullException(nameof(providerLock));
        var canonical = new
        {
            providerLock.SchemaVersion,
            providerLock.SemanticProfileId,
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
                    package.SignerFingerprint,
                    package.Publisher,
                    package.LicenseExpression,
                    package.Redistributable,
                    SupportedRuntimeIdentifiers = (package.SupportedRuntimeIdentifiers ?? Array.Empty<string>())
                        .OrderBy(static value => value, StringComparer.Ordinal),
                    Assemblies = (package.Assemblies ?? Array.Empty<PowerShellCompilationProviderAssembly>())
                        .OrderBy(static assembly => assembly.Path, StringComparer.Ordinal)
                        .Select(static assembly => new { assembly.Path, assembly.Sha256, assembly.AssemblyName, assembly.AssemblyVersion, assembly.PublicKeyToken }),
                    NativeAssets = (package.NativeAssets ?? Array.Empty<PowerShellCompilationProviderNativeAsset>())
                        .OrderBy(static asset => asset.Path, StringComparer.Ordinal)
                        .Select(static asset => new
                        {
                            asset.Path,
                            asset.Sha256,
                            asset.RuntimeIdentifier,
                            asset.FileName,
                            asset.Format,
                            asset.Architecture,
                            ImportedLibraries = (asset.ImportedLibraries ?? Array.Empty<string>()).OrderBy(static value => value, StringComparer.Ordinal)
                        }),
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

    private static ProviderPackage Read(
        string path,
        PowerShellCompilationProviderTrustPolicy policy,
        string semanticProfileId,
        string? runtimeIdentifier)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("PowerForge provider package was not found.", path);
        var packageBytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(packageBytes, writable: false);
        using var packageReader = new PackageArchiveReader(stream, leaveStreamOpen: true);
        var signature = packageReader.GetPrimarySignatureAsync(CancellationToken.None).GetAwaiter().GetResult();
        var signatureState = "Unsigned";
        var signerFingerprint = string.Empty;
        if (signature is not null)
        {
            packageReader.ValidateIntegrityAsync(signature.SignatureContent, CancellationToken.None).GetAwaiter().GetResult();
            signatureState = "ValidIntegrity";
            var certificate = signature.SignerInfo.Certificate
                ?? throw new InvalidOperationException($"Provider package '{path}' signature has no signing certificate.");
            signerFingerprint = Hash(certificate.RawData);
        }
        if (policy.RequirePackageSignature && signature is null)
            throw new InvalidOperationException($"Provider package '{path}' is unsigned, but policy requires a NuGet package signature.");
        if (policy.RequirePackageSignature && policy.AllowedSignerFingerprints.Length == 0)
            throw new InvalidOperationException("Signed provider-package trust requires at least one explicitly allowed signing-certificate fingerprint.");
        if (policy.AllowedSignerFingerprints.Length > 0 &&
            !Contains(policy.AllowedSignerFingerprints, signerFingerprint))
            throw new InvalidOperationException($"Provider package '{path}' signing-certificate fingerprint is not allowed by policy.");

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
        ValidateDistributionFields(manifestBytes, path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = false };
        options.Converters.Add(new JsonStringEnumConverter());
        var manifest = JsonSerializer.Deserialize<PowerShellCompilationProviderPackageManifest>(manifestBytes, options)
            ?? throw new InvalidOperationException($"Provider package '{path}' contains an empty manifest.");
        ValidateManifest(manifest, policy, path, semanticProfileId, runtimeIdentifier);
        PowerShellCompilationProviderContractValidator.Validate(manifest);
        ValidatePackageMetadata(packageReader, files, manifest, path);
        var assemblies = ValidateAssemblies(packageReader, files, manifest, path);
        var nativeAssets = ValidateNativeAssets(packageReader, files, manifest, path, runtimeIdentifier);
        ValidateAdapterEntryPoints(packageReader, manifest, path);

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
                SignerFingerprint = signerFingerprint,
                Publisher = manifest.Publisher,
                LicenseExpression = manifest.LicenseExpression,
                Redistributable = manifest.Redistributable,
                SupportedRuntimeIdentifiers = manifest.SupportedRuntimeIdentifiers
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray(),
                Assemblies = assemblies,
                NativeAssets = nativeAssets,
                Dependencies = manifest.Dependencies
                    .OrderBy(static dependency => dependency.PackageId, StringComparer.Ordinal)
                    .ToArray(),
                ProviderIds = manifest.Providers
                    .Select(static provider => provider.ProviderId)
                    .OrderBy(static id => id, StringComparer.Ordinal)
                    .ToArray()
            },
            assemblies.Select(assembly => new PowerShellCompilationResolvedProviderAssembly
            {
                PackageId = manifest.PackageId,
                PackagePath = Path.GetFullPath(path),
                Assembly = assembly
            }).ToArray(),
            nativeAssets.Select(asset => new PowerShellCompilationResolvedProviderNativeAsset
            {
                PackageId = manifest.PackageId,
                PackagePath = Path.GetFullPath(path),
                Asset = asset
            }).ToArray());
    }

    private static void ValidateManifest(
        PowerShellCompilationProviderPackageManifest manifest,
        PowerShellCompilationProviderTrustPolicy policy,
        string path,
        string semanticProfileId,
        string? runtimeIdentifier)
    {
        if (manifest.SchemaVersion != 3)
            throw new InvalidOperationException($"Provider package '{path}' uses unsupported manifest schema '{manifest.SchemaVersion}'; rebuild it with the current provider SDK so distribution trust is explicit.");
        if (!string.Equals(manifest.ProviderAbiVersion, PowerShellCompilationProviderAbi.CurrentVersion, StringComparison.Ordinal))
            throw new InvalidOperationException($"Provider package '{path}' targets ABI '{manifest.ProviderAbiVersion}', expected '{PowerShellCompilationProviderAbi.CurrentVersion}'.");
        Require(manifest.PackageId, "PackageId", path);
        Require(manifest.PackageVersion, "PackageVersion", path);
        Require(manifest.Publisher, "Publisher", path);
        Require(manifest.LicenseExpression, "LicenseExpression", path);
        if (policy.RequireRedistributable && !manifest.Redistributable)
            throw new InvalidOperationException($"Provider package '{path}' is not approved for redistribution by the selected trust policy.");
        if (manifest.SupportedRuntimeIdentifiers is null)
            throw new InvalidOperationException($"Provider package '{path}' has a null SupportedRuntimeIdentifiers collection.");
        var normalizedRuntimeIdentifiers = manifest.SupportedRuntimeIdentifiers
            .Select(static value => (value ?? string.Empty).Trim().ToLowerInvariant())
            .ToArray();
        if (normalizedRuntimeIdentifiers.Any(static value => value.Length == 0 ||
                                                           !System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9]+(?:[.-][a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
            throw new InvalidOperationException($"Provider package '{path}' contains an invalid supported runtime identifier.");
        if (!manifest.SupportedRuntimeIdentifiers.SequenceEqual(normalizedRuntimeIdentifiers, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider package '{path}' supported runtime identifiers must use canonical lowercase RID spelling without surrounding whitespace.");
        if (normalizedRuntimeIdentifiers.Distinct(StringComparer.Ordinal).Count() != normalizedRuntimeIdentifiers.Length)
            throw new InvalidOperationException($"Provider package '{path}' contains duplicate supported runtime identifiers.");
        var requestedRuntimeIdentifier = runtimeIdentifier?.Trim().ToLowerInvariant();
        if (normalizedRuntimeIdentifiers.Length > 0)
        {
            if (string.IsNullOrWhiteSpace(requestedRuntimeIdentifier))
                throw new InvalidOperationException($"Provider package '{path}' is restricted to exact runtime identifiers and cannot be resolved for a RID-less artifact target.");
            if (!normalizedRuntimeIdentifiers.Contains(requestedRuntimeIdentifier, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider package '{path}' does not support runtime identifier '{runtimeIdentifier}'.");
        }
        if (!Version.TryParse(manifest.PackageVersion, out var packageVersion) || packageVersion.Build < 0 || packageVersion.Revision >= 0)
            throw new InvalidOperationException($"Provider package '{path}' must use a three-part public PackageVersion.");
        ApplyIdentityPolicy(manifest, policy, path);
        if (manifest.SemanticProfiles is null || manifest.SemanticProfiles.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one semantic profile.");
        if (manifest.SourceSemanticProfiles is null || manifest.SourceSemanticProfiles.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one source semantic profile.");
        var sourceProfiles = manifest.SourceSemanticProfiles
            .Select(static profile => PowerShellCompilationSemanticOracleCatalog.Get(profile).ProfileId)
            .ToArray();
        if (!sourceProfiles.Contains(semanticProfileId, StringComparer.Ordinal))
            throw new InvalidOperationException($"Provider package '{path}' does not support source semantic profile '{semanticProfileId}'.");
        if (manifest.Assemblies is null || manifest.Assemblies.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one provider assembly.");
        if (manifest.NativeAssets is null)
            throw new InvalidOperationException($"Provider package '{path}' has a null NativeAssets collection.");
        if (manifest.NativeAssets.Length > 0 && normalizedRuntimeIdentifiers.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' carries native assets but declares no supported runtime identifier.");
        var duplicateNativePath = manifest.NativeAssets.GroupBy(static asset => NormalizePath(asset.Path), StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1);
        if (duplicateNativePath is not null)
            throw new InvalidOperationException($"Provider package '{path}' declares native asset path '{duplicateNativePath.Key}' more than once.");
        foreach (var asset in manifest.NativeAssets)
        {
            if (!normalizedRuntimeIdentifiers.Contains(asset.RuntimeIdentifier, StringComparer.Ordinal))
                throw new InvalidOperationException($"Provider package '{path}' native asset '{asset.Path}' targets undeclared runtime identifier '{asset.RuntimeIdentifier}'.");
        }
        if (manifest.Providers is null || manifest.Providers.Length == 0)
            throw new InvalidOperationException($"Provider package '{path}' must declare at least one provider contract.");

        var dependencies = manifest.Dependencies ?? Array.Empty<PowerShellCompilationProviderDependency>();
        var duplicateDependency = dependencies
            .GroupBy(static dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateDependency is not null)
            throw new InvalidOperationException(
                $"Provider package '{path}' declares dependency '{duplicateDependency.Key}' more than once.");
        foreach (var dependency in dependencies)
        {
            Require(dependency.PackageId, "Dependency.PackageId", path);
            Require(dependency.Version, "Dependency.Version", path);
            Require(dependency.ContentHash, "Dependency.ContentHash", path);
            if (!Version.TryParse(dependency.Version, out var dependencyVersion) || dependencyVersion.Build < 0 || dependencyVersion.Revision >= 0)
                throw new InvalidOperationException($"Provider package '{path}' dependency '{dependency.PackageId}' must use an exact three-part public version.");
        }
        foreach (var provider in manifest.Providers)
            ApplyProviderPolicy(provider.ProviderId, policy, path);
    }

    private static void ValidateDistributionFields(byte[] manifestBytes, string path)
    {
        using var document = JsonDocument.Parse(manifestBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Provider package '{path}' manifest must be a JSON object.");
        var properties = document.RootElement.EnumerateObject().ToArray();
        var redistribution = properties.Where(static property => property.NameEquals("Redistributable")).ToArray();
        var runtimeIdentifiers = properties.Where(static property => property.NameEquals("SupportedRuntimeIdentifiers")).ToArray();
        if (redistribution.Length != 1 || redistribution[0].Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            runtimeIdentifiers.Length != 1 || runtimeIdentifiers[0].Value.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException(
                $"Provider package '{path}' must explicitly declare one Boolean Redistributable field and one SupportedRuntimeIdentifiers array; rebuild it with the current provider SDK.");
    }

    private static void ValidateAdapterEntryPoints(
        PackageArchiveReader packageReader,
        PowerShellCompilationProviderPackageManifest manifest,
        string packagePath)
    {
        foreach (var provider in manifest.Providers.Where(static provider => provider.Adapter?.EntryPoint is not null))
        {
            var entryPoint = provider.Adapter.EntryPoint!;
            var assemblyPath = NormalizePath(entryPoint.AssemblyPath);
            if (!manifest.Assemblies.Any(assembly => NormalizePath(assembly.Path).Equals(assemblyPath, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' entry point references undeclared assembly '{entryPoint.AssemblyPath}'.");
            if (!IsQualifiedIdentifier(entryPoint.TypeName) || !IsIdentifier(entryPoint.MethodName))
                throw new InvalidOperationException($"Provider '{provider.ProviderId}' entry point is not a safe CLR/C# identifier.");
            byte[] bytes;
            using (var source = packageReader.GetStream(assemblyPath))
            using (var memory = new MemoryStream())
            {
                source.CopyTo(memory);
                bytes = memory.ToArray();
            }
            using var stream = new MemoryStream(bytes, writable: false);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            var reader = peReader.GetMetadataReader();
            var matches = new List<MethodDefinition>();
            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                var typeName = reader.GetString(type.Name);
                var typeNamespace = reader.GetString(type.Namespace);
                var fullName = string.IsNullOrWhiteSpace(typeNamespace) ? typeName : typeNamespace + "." + typeName;
                if (!fullName.Equals(entryPoint.TypeName, StringComparison.Ordinal) ||
                    (type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
                    (type.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
                    continue;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (reader.GetString(method.Name).Equals(entryPoint.MethodName, StringComparison.Ordinal) &&
                        (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public &&
                        (method.Attributes & MethodAttributes.Static) != 0 &&
                        (method.Attributes & MethodAttributes.Abstract) == 0)
                        matches.Add(method);
                }
            }
            var collection = provider.Stream.Equals("Success", StringComparison.Ordinal) &&
                             provider.Cardinality == PowerShellCompilationCommandCardinality.Collection;
            var cooperative = provider.Adapter.Cancellation is
                PowerShellCompilationProviderCancellation.Cooperative or
                PowerShellCompilationProviderCancellation.PostInitializationCooperative;
            if (matches.Count != 1 || !HasTransformSignature(reader, matches[0], collection, entryPoint.ResultType, cooperative))
                throw new InvalidOperationException(
                    $"Provider '{provider.ProviderId}' entry point must be one public static non-generic " +
                    (collection
                        ? $"{GetValueTypeName(entryPoint.ResultType)}[] Method(string{(cooperative ? ", CancellationToken" : string.Empty)})."
                        : $"{GetValueTypeName(entryPoint.ResultType)} Method(string{(cooperative ? ", CancellationToken" : string.Empty)})."));
        }
    }

    private static bool HasTransformSignature(
        MetadataReader reader,
        MethodDefinition method,
        bool collection,
        PowerShellCompilationProviderValueType resultType,
        bool cooperative)
    {
        var blob = reader.GetBlobReader(method.Signature);
        var header = blob.ReadSignatureHeader();
        if (header.IsGeneric) return false;
        if (blob.ReadCompressedInteger() != (cooperative ? 2 : 1)) return false;
        if (collection)
        {
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.SZArray ||
                blob.ReadSignatureTypeCode() != GetSignatureTypeCode(resultType))
                return false;
        }
        else if (blob.ReadSignatureTypeCode() != GetSignatureTypeCode(resultType))
        {
            return false;
        }
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.String) return false;
        if (!cooperative) return true;
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.TypeHandle) return false;
        var codedIndex = blob.ReadCompressedInteger();
        if (codedIndex < 0) return false;
        var row = codedIndex >> 2;
        return (codedIndex & 3) switch
        {
            0 => false,
            1 => IsFrameworkCancellationToken(reader, MetadataTokens.TypeReferenceHandle(row)),
            _ => false
        };
    }

    private static bool IsFrameworkCancellationToken(MetadataReader reader, TypeReferenceHandle handle)
    {
        var type = reader.GetTypeReference(handle);
        if (!reader.GetString(type.Namespace).Equals("System.Threading", StringComparison.Ordinal) ||
            !reader.GetString(type.Name).Equals("CancellationToken", StringComparison.Ordinal) ||
            type.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return false;
        var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
        var name = reader.GetString(assembly.Name);
        var publicKeyToken = BitConverter.ToString(reader.GetBlobBytes(assembly.PublicKeyOrToken))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
        return (name, publicKeyToken) switch
        {
            ("System.Runtime", "b03f5f7f11d50a3a") => true,
            ("mscorlib", "b77a5c561934e089") => true,
            ("System.Private.CoreLib", "7cec85d7bea7798e") => true,
            ("netstandard", "cc7b13ffcd2ddd51") => true,
            _ => false
        };
    }

    private static SignatureTypeCode GetSignatureTypeCode(PowerShellCompilationProviderValueType valueType)
        => valueType switch
        {
            PowerShellCompilationProviderValueType.String => SignatureTypeCode.String,
            PowerShellCompilationProviderValueType.Int32 => SignatureTypeCode.Int32,
            PowerShellCompilationProviderValueType.Int64 => SignatureTypeCode.Int64,
            PowerShellCompilationProviderValueType.Double => SignatureTypeCode.Double,
            PowerShellCompilationProviderValueType.Boolean => SignatureTypeCode.Boolean,
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Provider result type is not defined.")
        };

    private static string GetValueTypeName(PowerShellCompilationProviderValueType valueType)
        => valueType switch
        {
            PowerShellCompilationProviderValueType.String => "string",
            PowerShellCompilationProviderValueType.Int32 => "int",
            PowerShellCompilationProviderValueType.Int64 => "long",
            PowerShellCompilationProviderValueType.Double => "double",
            PowerShellCompilationProviderValueType.Boolean => "bool",
            _ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "Provider result type is not defined.")
        };

    private static bool IsQualifiedIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Split('.').All(IsIdentifier);

    private static bool IsIdentifier(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           (char.IsLetter(value[0]) || value[0] == '_') &&
           value.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static void ValidatePackageMetadata(
        PackageArchiveReader packageReader,
        IReadOnlyCollection<string> files,
        PowerShellCompilationProviderPackageManifest manifest,
        string packagePath)
    {
        var nuspecPaths = files.Where(static file => file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (nuspecPaths.Length != 1)
            throw new InvalidOperationException($"Provider package '{packagePath}' must contain exactly one NuGet manifest.");
        using var nuspecStream = packageReader.GetStream(nuspecPaths[0]);
        using var xmlReader = XmlReader.Create(nuspecStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var document = XDocument.Load(xmlReader, LoadOptions.None);
        var metadata = document.Root?.Elements().FirstOrDefault(static element => element.Name.LocalName == "metadata")
            ?? throw new InvalidOperationException($"Provider package '{packagePath}' has no NuGet metadata element.");
        string Value(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? string.Empty;
        var license = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "license");
        if (!Value("id").Equals(manifest.PackageId, StringComparison.Ordinal) ||
            !Value("version").Equals(manifest.PackageVersion, StringComparison.Ordinal) ||
            !Value("authors").Equals(manifest.Publisher, StringComparison.Ordinal) ||
            license is null ||
            !string.Equals(license.Attribute("type")?.Value, "expression", StringComparison.OrdinalIgnoreCase) ||
            !license.Value.Trim().Equals(manifest.LicenseExpression, StringComparison.Ordinal))
            throw new InvalidOperationException($"Provider package '{packagePath}' manifest identity, publisher, or license conflicts with its NuGet metadata.");

        var nuspecDependencies = metadata
            .Descendants()
            .Where(static element => element.Name.LocalName == "dependency")
            .Select(element => new
            {
                PackageId = element.Attribute("id")?.Value.Trim() ?? string.Empty,
                Version = element.Attribute("version")?.Value.Trim() ?? string.Empty
            })
            .ToArray();
        if (nuspecDependencies.Any(static dependency => string.IsNullOrWhiteSpace(dependency.PackageId) ||
                                                        string.IsNullOrWhiteSpace(dependency.Version)) ||
            nuspecDependencies.GroupBy(static dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase)
                .Any(static group => group.Count() > 1))
            throw new InvalidOperationException($"Provider package '{packagePath}' contains ambiguous NuGet dependency metadata.");
        var declaredDependencies = (manifest.Dependencies ?? Array.Empty<PowerShellCompilationProviderDependency>())
            .ToDictionary(static dependency => dependency.PackageId, StringComparer.OrdinalIgnoreCase);
        if (nuspecDependencies.Length != declaredDependencies.Count ||
            nuspecDependencies.Any(dependency =>
                !declaredDependencies.TryGetValue(dependency.PackageId, out var declared) ||
                !dependency.Version.Equals("[" + declared.Version + "]", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                $"Provider package '{packagePath}' dependency identities or exact versions conflict with its NuGet metadata.");
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
        internal ProviderPackage(
            PowerShellCompilationCommandProviderContract[] providers,
            PowerShellCompilationProviderPackageLockEntry providerLock,
            PowerShellCompilationResolvedProviderAssembly[] runtimeAssemblies,
            PowerShellCompilationResolvedProviderNativeAsset[] runtimeNativeAssets)
        {
            Providers = providers;
            Lock = providerLock;
            RuntimeAssemblies = runtimeAssemblies;
            RuntimeNativeAssets = runtimeNativeAssets;
        }

        internal PowerShellCompilationCommandProviderContract[] Providers { get; }
        internal PowerShellCompilationProviderPackageLockEntry Lock { get; }
        internal PowerShellCompilationResolvedProviderAssembly[] RuntimeAssemblies { get; }
        internal PowerShellCompilationResolvedProviderNativeAsset[] RuntimeNativeAssets { get; }
    }
}
