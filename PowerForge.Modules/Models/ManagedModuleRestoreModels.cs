using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Controls resolution and application of a module dependency lock.</summary>
/// <param name="requirementsPath">JSON file containing the root module requirements.</param>
/// <param name="lockPath">Path to the dependency lock.</param>
/// <param name="destination">Directory that will hold the installed modules.</param>
/// <param name="source">HTTP(S) repository URL or local repository folder.</param>
/// <param name="writeLock">Resolve a new lock instead of consuming the existing lock.</param>
/// <param name="plan">Verify and report the planned packages without writing the destination or lock.</param>
/// <param name="acceptLicense">Accept licenses that require explicit agreement.</param>
/// <param name="allowClobber">Permit installing commands that overlap existing modules.</param>
public sealed class ManagedModuleRestoreRequest(
    string requirementsPath,
    string lockPath,
    string destination,
    string source,
    bool writeLock,
    bool plan,
    bool acceptLicense,
    bool allowClobber)
{
    /// <summary>Gets the root requirements path.</summary>
    public string RequirementsPath { get; } = requirementsPath;
    /// <summary>Gets the lock file path.</summary>
    public string LockPath { get; } = lockPath;
    /// <summary>Gets the module installation directory.</summary>
    public string Destination { get; } = destination;
    /// <summary>Gets the repository source.</summary>
    public string Source { get; } = source;
    /// <summary>Gets whether to resolve a new dependency lock.</summary>
    public bool WriteLock { get; } = writeLock;
    /// <summary>Gets whether to verify a plan without applying changes.</summary>
    public bool Plan { get; } = plan;
    /// <summary>Gets whether explicit license acceptance is allowed.</summary>
    public bool AcceptLicense { get; } = acceptLicense;
    /// <summary>Gets whether overlapping commands are allowed.</summary>
    public bool AllowClobber { get; } = allowClobber;
}

/// <summary>Records the outcome of a module restore or plan.</summary>
/// <param name="destination">Resolved module directory.</param>
/// <param name="lockPath">Resolved path to the lock.</param>
/// <param name="plan">Whether the operation was a plan.</param>
/// <param name="lockWritten">Whether the operation wrote a lock.</param>
/// <param name="restoreLock">Verified module package pins.</param>
public sealed class ManagedModuleRestoreResult(
    string destination,
    string lockPath,
    bool plan,
    bool lockWritten,
    ManagedModuleRestoreLock restoreLock)
{
    /// <summary>Gets the module installation directory.</summary>
    public string Destination { get; } = destination;
    /// <summary>Gets the lock file path.</summary>
    public string LockPath { get; } = lockPath;
    /// <summary>Gets whether to verify a plan without applying changes.</summary>
    public bool Plan { get; } = plan;
    /// <summary>Gets whether the lock file was written.</summary>
    public bool LockWritten { get; } = lockWritten;
    /// <summary>Gets the package pins prepared by restore.</summary>
    public ManagedModuleRestoreLock Lock { get; } = restoreLock;
}

/// <summary>Describes an entire locked dependency graph and its source.</summary>
/// <param name="schemaVersion">Lock schema version.</param>
/// <param name="source">Repository identity.</param>
/// <param name="requirements">Root constraints used at resolution time.</param>
/// <param name="packages">Exact package versions and hashes in the dependency closure.</param>
public sealed class ManagedModuleRestoreLock(
    int schemaVersion,
    string source,
    ManagedModuleRestoreRequirement[] requirements,
    ManagedModuleRestorePackage[] packages)
{
    /// <summary>Gets the lock schema version.</summary>
    public int SchemaVersion { get; } = schemaVersion;
    /// <summary>Gets the repository source.</summary>
    public string Source { get; } = source;
    /// <summary>Gets the root requirements.</summary>
    public ManagedModuleRestoreRequirement[] Requirements { get; } = requirements;
    /// <summary>Gets the pinned package closure.</summary>
    public ManagedModuleRestorePackage[] Packages { get; } = packages;
}

/// <summary>Specifies one root requirement for a restore.</summary>
/// <param name="name">Module name.</param>
/// <param name="version">Version constraint.</param>
public sealed class ManagedModuleRestoreRequirement(string name, string version)
{
    /// <summary>Gets the module name.</summary>
    public string Name { get; } = name;
    /// <summary>Gets the version requirement or exact package version.</summary>
    public string Version { get; } = version;
}

/// <summary>Pins one package by name, version and content hash.</summary>
/// <param name="name">Module name.</param>
/// <param name="version">Exact package version.</param>
/// <param name="sha256">Expected package SHA-256 in hexadecimal.</param>
public sealed class ManagedModuleRestorePackage(string name, string version, string sha256)
{
    /// <summary>Gets the module name.</summary>
    public string Name { get; } = name;
    /// <summary>Gets the version requirement or exact package version.</summary>
    public string Version { get; } = version;
    /// <summary>Gets the expected package content hash.</summary>
    public string Sha256 { get; } = sha256;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ManagedModuleRestoreLock))]
internal sealed partial class ManagedModuleRestoreJsonContext : JsonSerializerContext;
