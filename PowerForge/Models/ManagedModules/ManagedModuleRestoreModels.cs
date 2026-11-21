using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ManagedModuleRestoreRequest(
    string requirementsPath,
    string lockPath,
    string destination,
    string source,
    bool writeLock,
    bool plan,
    bool acceptLicense,
    bool allowClobber)
{
    public string RequirementsPath { get; } = requirementsPath;
    public string LockPath { get; } = lockPath;
    public string Destination { get; } = destination;
    public string Source { get; } = source;
    public bool WriteLock { get; } = writeLock;
    public bool Plan { get; } = plan;
    public bool AcceptLicense { get; } = acceptLicense;
    public bool AllowClobber { get; } = allowClobber;
}

internal sealed class ManagedModuleRestoreResult(
    string destination,
    string lockPath,
    bool plan,
    bool lockWritten,
    ManagedModuleRestoreLock restoreLock)
{
    public string Destination { get; } = destination;
    public string LockPath { get; } = lockPath;
    public bool Plan { get; } = plan;
    public bool LockWritten { get; } = lockWritten;
    public ManagedModuleRestoreLock Lock { get; } = restoreLock;
}

internal sealed class ManagedModuleRestoreLock(
    int schemaVersion,
    string source,
    ManagedModuleRestoreRequirement[] requirements,
    ManagedModuleRestorePackage[] packages)
{
    public int SchemaVersion { get; } = schemaVersion;
    public string Source { get; } = source;
    public ManagedModuleRestoreRequirement[] Requirements { get; } = requirements;
    public ManagedModuleRestorePackage[] Packages { get; } = packages;
}

internal sealed class ManagedModuleRestoreRequirement(string name, string version)
{
    public string Name { get; } = name;
    public string Version { get; } = version;
}

internal sealed class ManagedModuleRestorePackage(string name, string version, string sha256)
{
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string Sha256 { get; } = sha256;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ManagedModuleRestoreLock))]
internal sealed partial class ManagedModuleRestoreJsonContext : JsonSerializerContext;
