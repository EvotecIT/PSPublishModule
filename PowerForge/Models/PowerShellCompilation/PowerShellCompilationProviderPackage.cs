using System;

namespace PowerForge;

/// <summary>Provider-package ABI owned by PowerForge and negotiated before provider metadata is accepted.</summary>
public static class PowerShellCompilationProviderAbi
{
    /// <summary>Current provider package ABI version.</summary>
    public const string CurrentVersion = "4";
}

/// <summary>One validated provider assembly available to generated projects but never loaded by the compiler.</summary>
public sealed class PowerShellCompilationResolvedProviderAssembly
{
    /// <summary>Provider package identity.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Full source package path.</summary>
    public string PackagePath { get; set; } = string.Empty;

    /// <summary>Locked package-relative assembly evidence.</summary>
    public PowerShellCompilationProviderAssembly Assembly { get; set; } = new();
}

/// <summary>One validated RID-specific native asset delivered by a provider package.</summary>
public sealed class PowerShellCompilationResolvedProviderNativeAsset
{
    /// <summary>Provider package identity.</summary>
    public string PackageId { get; set; } = string.Empty;
    /// <summary>Full source package path.</summary>
    public string PackagePath { get; set; } = string.Empty;
    /// <summary>Locked package-relative native-asset evidence.</summary>
    public PowerShellCompilationProviderNativeAsset Asset { get; set; } = new();
}

/// <summary>One managed assembly delivered by a provider package.</summary>
public sealed class PowerShellCompilationProviderAssembly
{
    /// <summary>Package-relative assembly path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Assembly file SHA-256.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Declared managed assembly name.</summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>Declared managed assembly version.</summary>
    public string AssemblyVersion { get; set; } = string.Empty;

    /// <summary>Declared public-key token, or empty for an unsigned assembly.</summary>
    public string PublicKeyToken { get; set; } = string.Empty;
}

/// <summary>One exact RID-specific native runtime asset carried by a provider package.</summary>
public sealed class PowerShellCompilationProviderNativeAsset
{
    /// <summary>Package-relative asset path.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Asset file SHA-256.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>Exact runtime identifier for which the asset is valid.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;
    /// <summary>File name used beside the generated artifact.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Inspected native container format: PE, ELF, or MachO.</summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>Architecture encoded in the native header.</summary>
    public string Architecture { get; set; } = string.Empty;
    /// <summary>Exact native libraries declared by the import/load table.</summary>
    public string[] ImportedLibraries { get; set; } = Array.Empty<string>();
}

/// <summary>One exact transitive dependency declared by a provider package.</summary>
public sealed class PowerShellCompilationProviderDependency
{
    /// <summary>Package identity.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Exact three-part public package version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Reviewed NuGet content identity. Project restore reconciles it with NuGet's resolved lock and acquired package bytes.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;
}

/// <summary>
/// Deterministic metadata embedded at <c>powerforge/provider.json</c> in a provider package.
/// The compiler reads this document directly and never executes provider assemblies during discovery.
/// </summary>
public sealed class PowerShellCompilationProviderPackageManifest
{
    /// <summary>Provider-package manifest schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>PowerForge provider ABI version.</summary>
    public string ProviderAbiVersion { get; set; } = PowerShellCompilationProviderAbi.CurrentVersion;

    /// <summary>NuGet-style package identity.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Exact public package version.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Publisher identity asserted by package metadata and policy.</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>SPDX-compatible license expression.</summary>
    public string LicenseExpression { get; set; } = string.Empty;

    /// <summary>Whether the reviewed package license and publisher policy permit redistribution in generated artifacts.</summary>
    public bool Redistributable { get; set; }

    /// <summary>Optional exact runtime identifiers on which this provider package may be delivered. Empty means RID-portable.</summary>
    public string[] SupportedRuntimeIdentifiers { get; set; } = Array.Empty<string>();

    /// <summary>Semantic profiles accepted by this package.</summary>
    public string[] SemanticProfiles { get; set; } = Array.Empty<string>();

    /// <summary>Named PowerShell source semantic profiles for which these contracts are valid.</summary>
    public string[] SourceSemanticProfiles { get; set; } = Array.Empty<string>();

    /// <summary>Managed provider assemblies carried by the package.</summary>
    public PowerShellCompilationProviderAssembly[] Assemblies { get; set; } = Array.Empty<PowerShellCompilationProviderAssembly>();

    /// <summary>RID-specific native runtime assets carried by the package.</summary>
    public PowerShellCompilationProviderNativeAsset[] NativeAssets { get; set; } = Array.Empty<PowerShellCompilationProviderNativeAsset>();

    /// <summary>Exact transitive package closure required by the provider.</summary>
    public PowerShellCompilationProviderDependency[] Dependencies { get; set; } = Array.Empty<PowerShellCompilationProviderDependency>();

    /// <summary>Compile-time-only command contracts supplied by the package.</summary>
    public PowerShellCompilationCommandProviderContract[] Providers { get; set; } = Array.Empty<PowerShellCompilationCommandProviderContract>();
}

/// <summary>Explicit provider-package input selected for one compiler invocation.</summary>
public sealed class PowerShellCompilationProviderPackageReference
{
    /// <summary>Creates an explicit provider-package reference.</summary>
    public PowerShellCompilationProviderPackageReference(string path)
    {
        Path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException("A provider package path is required.", nameof(path))
            : System.IO.Path.GetFullPath(path.Trim().Trim('"'));
    }

    /// <summary>Full path to a provider package.</summary>
    public string Path { get; }
}

/// <summary>Trust and allow/deny policy applied to provider packages before their contracts enter analysis.</summary>
public sealed class PowerShellCompilationProviderTrustPolicy
{
    /// <summary>Package identities explicitly allowed. Empty means no allow-list restriction.</summary>
    public string[] AllowedPackageIds { get; set; } = Array.Empty<string>();

    /// <summary>Package identities explicitly denied. Deny rules take precedence.</summary>
    public string[] DeniedPackageIds { get; set; } = Array.Empty<string>();

    /// <summary>Provider identities explicitly allowed. Empty means no allow-list restriction.</summary>
    public string[] AllowedProviderIds { get; set; } = Array.Empty<string>();

    /// <summary>Provider identities explicitly denied. Deny rules take precedence.</summary>
    public string[] DeniedProviderIds { get; set; } = Array.Empty<string>();

    /// <summary>Accepted publisher identities. Empty means no publisher allow-list restriction.</summary>
    public string[] AllowedPublishers { get; set; } = Array.Empty<string>();

    /// <summary>Accepted license expressions. Empty means no license allow-list restriction.</summary>
    public string[] AllowedLicenseExpressions { get; set; } = Array.Empty<string>();

    /// <summary>Accepted SHA-256 fingerprints of NuGet signing certificates. Empty means no signer allow-list restriction.</summary>
    public string[] AllowedSignerFingerprints { get; set; } = Array.Empty<string>();

    /// <summary>Whether an unsigned provider package is rejected.</summary>
    public bool RequirePackageSignature { get; set; }

    /// <summary>Whether provider packages must explicitly declare reviewed redistribution permission.</summary>
    public bool RequireRedistributable { get; set; }
}

/// <summary>Locked identity and trust evidence for one provider package.</summary>
public sealed class PowerShellCompilationProviderPackageLockEntry
{
    /// <summary>Package identity.</summary>
    public string PackageId { get; set; } = string.Empty;

    /// <summary>Exact package version.</summary>
    public string PackageVersion { get; set; } = string.Empty;

    /// <summary>Provider ABI version.</summary>
    public string ProviderAbiVersion { get; set; } = string.Empty;

    /// <summary>Full package SHA-256.</summary>
    public string PackageSha256 { get; set; } = string.Empty;

    /// <summary>Embedded provider-manifest SHA-256.</summary>
    public string ManifestSha256 { get; set; } = string.Empty;

    /// <summary>NuGet signature state: Valid, Invalid, or Unsigned.</summary>
    public string Signature { get; set; } = "Unsigned";

    /// <summary>SHA-256 fingerprint of the package signing certificate, or empty for an unsigned package.</summary>
    public string SignerFingerprint { get; set; } = string.Empty;

    /// <summary>Publisher identity.</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>License expression.</summary>
    public string LicenseExpression { get; set; } = string.Empty;

    /// <summary>Reviewed redistribution disposition copied from the canonical provider manifest.</summary>
    public bool Redistributable { get; set; }

    /// <summary>Exact supported runtime identifiers copied into the reviewed lock. Empty means RID-portable.</summary>
    public string[] SupportedRuntimeIdentifiers { get; set; } = Array.Empty<string>();

    /// <summary>Exact assembly closure.</summary>
    public PowerShellCompilationProviderAssembly[] Assemblies { get; set; } = Array.Empty<PowerShellCompilationProviderAssembly>();

    /// <summary>Exact RID-specific native runtime assets.</summary>
    public PowerShellCompilationProviderNativeAsset[] NativeAssets { get; set; } = Array.Empty<PowerShellCompilationProviderNativeAsset>();

    /// <summary>Exact package dependency closure.</summary>
    public PowerShellCompilationProviderDependency[] Dependencies { get; set; } = Array.Empty<PowerShellCompilationProviderDependency>();

    /// <summary>Accepted provider identities.</summary>
    public string[] ProviderIds { get; set; } = Array.Empty<string>();
}

/// <summary>Deterministic provider package lock consumed by analysis and artifact publication.</summary>
public sealed class PowerShellCompilationProviderLock
{
    /// <summary>Provider-lock schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Exact PowerShell source semantic profile used to select this provider set.</summary>
    public string SemanticProfileId { get; set; } = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;

    /// <summary>Locked provider packages ordered by package identity.</summary>
    public PowerShellCompilationProviderPackageLockEntry[] Packages { get; set; } = Array.Empty<PowerShellCompilationProviderPackageLockEntry>();

    /// <summary>SHA-256 over the canonical provider lock.</summary>
    public string LockSha256 { get; set; } = string.Empty;
}

/// <summary>Non-executing provider package discovery result.</summary>
public sealed class PowerShellCompilationProviderResolution
{
    /// <summary>Validated compile-time provider contracts.</summary>
    public PowerShellCompilationCommandProviderContract[] Providers { get; set; } = Array.Empty<PowerShellCompilationCommandProviderContract>();

    /// <summary>Deterministic trust lock.</summary>
    public PowerShellCompilationProviderLock Lock { get; set; } = new();

    /// <summary>Validated runtime assemblies to extract into generated projects without loading them in the compiler.</summary>
    public PowerShellCompilationResolvedProviderAssembly[] RuntimeAssemblies { get; set; } = Array.Empty<PowerShellCompilationResolvedProviderAssembly>();

    /// <summary>Validated native runtime assets to extract without executing package content.</summary>
    public PowerShellCompilationResolvedProviderNativeAsset[] RuntimeNativeAssets { get; set; } = Array.Empty<PowerShellCompilationResolvedProviderNativeAsset>();
}
