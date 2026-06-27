namespace PowerForge;

internal sealed class ModuleStatePlanAction
{
    internal ModuleStatePlanAction(
        ModuleStatePlanActionKind kind,
        string moduleName,
        string? installedVersion,
        string versionPolicy,
        string reason,
        bool isRepair = false,
        bool force = false,
        string? targetScope = null,
        string? targetPath = null,
        string? targetRepository = null)
    {
        Kind = kind;
        ModuleName = moduleName;
        InstalledVersion = installedVersion;
        VersionPolicy = versionPolicy;
        Reason = reason;
        IsRepair = isRepair;
        Force = force;
        TargetScope = string.IsNullOrWhiteSpace(targetScope) ? null : targetScope!.Trim();
        TargetPath = string.IsNullOrWhiteSpace(targetPath) ? null : targetPath!.Trim();
        TargetRepository = string.IsNullOrWhiteSpace(targetRepository) ? null : targetRepository!.Trim();
    }

    internal ModuleStatePlanActionKind Kind { get; }

    internal string ModuleName { get; }

    internal string? InstalledVersion { get; }

    internal string VersionPolicy { get; }

    internal string Reason { get; }

    internal bool IsRepair { get; }

    internal bool Force { get; }

    internal string? TargetScope { get; }

    internal string? TargetPath { get; }

    internal string? TargetRepository { get; }
}

internal enum ModuleStatePlanActionKind
{
    NoAction,
    Install,
    Update,
    Save,
    Remove
}
