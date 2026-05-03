namespace PowerForge.Web.Cli;

internal sealed class PowerForgeServerRecoveryManifest
{
    public int SchemaVersion { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public PowerForgeServerTarget? Target { get; set; }
    public PowerForgeServerRepository[]? Repositories { get; set; }
    public PowerForgeServerPackages? Packages { get; set; }
    public PowerForgeServerPath[]? Paths { get; set; }
    public PowerForgeServerApache? Apache { get; set; }
    public PowerForgeServerFirewall? Firewall { get; set; }
    public PowerForgeServerSystemd? Systemd { get; set; }
    public PowerForgeServerCertificate[]? Certificates { get; set; }
    public PowerForgeServerSecret[]? Secrets { get; set; }
    public PowerForgeServerCapture? Capture { get; set; }
    public PowerForgeServerCommandGroup? Bootstrap { get; set; }
    public PowerForgeServerCommandGroup? Deploy { get; set; }
    public PowerForgeServerVerify? Verify { get; set; }
    public PowerForgeServerBackupTarget? BackupTarget { get; set; }
}

internal sealed class PowerForgeServerTarget
{
    public string? SshAlias { get; set; }
    public string? Host { get; set; }
    public string? User { get; set; }
    public int? SshPort { get; set; }
    public string? Os { get; set; }
}

internal sealed class PowerForgeServerRepository
{
    public string? Role { get; set; }
    public string? Url { get; set; }
    public string? Path { get; set; }
    public string? Branch { get; set; }
    public bool Required { get; set; }
}

internal sealed class PowerForgeServerPackages
{
    public string[]? Apt { get; set; }
    public string[]? ApacheModules { get; set; }
    public string[]? DotnetSdks { get; set; }
    public bool Powershell { get; set; }
}

internal sealed class PowerForgeServerPath
{
    public string? Id { get; set; }
    public string? Path { get; set; }
    public string? Owner { get; set; }
    public string? Group { get; set; }
    public string? Mode { get; set; }
    public string? Kind { get; set; }
}

internal sealed class PowerForgeServerApache
{
    public string? Service { get; set; }
    public string[]? Modules { get; set; }
    public PowerForgeServerManagedFile[]? Sites { get; set; }
    public PowerForgeServerManagedFile[]? Conf { get; set; }
    public string? ValidateCommand { get; set; }
    public string? ReloadCommand { get; set; }
}

internal sealed class PowerForgeServerFirewall
{
    public string? Provider { get; set; }
    public string? DefaultIncoming { get; set; }
    public string? DefaultOutgoing { get; set; }
    public int[]? SshPorts { get; set; }
    public string? WebOriginPolicy { get; set; }
    public string? SyncCommand { get; set; }
}

internal sealed class PowerForgeServerSystemd
{
    public PowerForgeServerSystemdUnit[]? Services { get; set; }
    public PowerForgeServerSystemdUnit[]? Timers { get; set; }
}

internal sealed class PowerForgeServerSystemdUnit
{
    public string? Name { get; set; }
    public string? Source { get; set; }
    public string? Target { get; set; }
    public bool Enabled { get; set; }
    public bool Required { get; set; }
}

internal sealed class PowerForgeServerCertificate
{
    public string? Name { get; set; }
    public string[]? Domains { get; set; }
    public string? Authenticator { get; set; }
    public string? CredentialsPath { get; set; }
    public string? RenewalConfigPath { get; set; }
    public string? DryRunCommand { get; set; }
    public string[]? SecretRefs { get; set; }
}

internal sealed class PowerForgeServerSecret
{
    public string Id { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Env { get; set; }
    public string[]? RequiredFor { get; set; }
    public string Capture { get; set; } = string.Empty;
    public string? RestoreMode { get; set; }
}

internal sealed class PowerForgeServerCapture
{
    public PowerForgeServerManagedFile[]? PlainFiles { get; set; }
    public PowerForgeServerManagedFile[]? EncryptedFiles { get; set; }
    public PowerForgeServerNamedCommand[]? Commands { get; set; }
    public string[]? Exclude { get; set; }
}

internal sealed class PowerForgeServerManagedFile
{
    public string? Source { get; set; }
    public string? Target { get; set; }
    public bool Required { get; set; }
    public bool Sensitive { get; set; }
}

internal sealed class PowerForgeServerCommandGroup
{
    public PowerForgeServerNamedCommand[]? Commands { get; set; }
}

internal sealed class PowerForgeServerNamedCommand
{
    public string? Id { get; set; }
    public string? Command { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool Sensitive { get; set; }
    public bool Required { get; set; }
}

internal sealed class PowerForgeServerVerify
{
    public PowerForgeServerNamedCommand[]? Commands { get; set; }
    public PowerForgeServerVerifyUrl[]? Urls { get; set; }
}

internal sealed class PowerForgeServerVerifyUrl
{
    public string? Url { get; set; }
    public int? ExpectedStatus { get; set; }
    public string? Via { get; set; }
}

internal sealed class PowerForgeServerBackupTarget
{
    public string? Type { get; set; }
    public string? Repository { get; set; }
    public string? Branch { get; set; }
    public string? Path { get; set; }
    public string? Encryption { get; set; }
    public string? Recipient { get; set; }
    public string? RecipientEnv { get; set; }
    public PowerForgeServerBackupRetention? Retention { get; set; }
}

internal sealed class PowerForgeServerBackupRetention
{
    public int? KeepLatest { get; set; }
    public int? KeepDays { get; set; }
}

internal sealed class PowerForgeServerRecoveryPlanResult
{
    public string? ManifestPath { get; set; }
    public string? Name { get; set; }
    public string? TargetHost { get; set; }
    public string? SshAlias { get; set; }
    public int? SshPort { get; set; }
    public int RepositoryCount { get; set; }
    public int PackageCount { get; set; }
    public int ApacheModuleCount { get; set; }
    public int SystemdServiceCount { get; set; }
    public int SystemdTimerCount { get; set; }
    public int CertificateCount { get; set; }
    public int PlainCaptureCount { get; set; }
    public int EncryptedCaptureCount { get; set; }
    public int SecretCount { get; set; }
    public string? BackupTarget { get; set; }
    public string? BackupEncryption { get; set; }
    public string[]? Stages { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerCaptureResult
{
    public string? ManifestPath { get; set; }
    public string? OutputPath { get; set; }
    public string? PlainArchivePath { get; set; }
    public string? EncryptedArchivePath { get; set; }
    public string? RestoreChecklistPath { get; set; }
    public PowerForgeServerCaptureCommandResult[]? CommandResults { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerCaptureCommandResult
{
    public string? Id { get; set; }
    public string? Command { get; set; }
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public string? StdoutPath { get; set; }
    public string? StderrPath { get; set; }
}

internal sealed class PowerForgeServerInspectResult
{
    public string? ManifestPath { get; set; }
    public string? Target { get; set; }
    public bool Success { get; set; }
    public PowerForgeServerInspectCheck[]? Checks { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerInspectCheck
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Expected { get; set; }
    public string? Actual { get; set; }
}

internal sealed class PowerForgeServerVerifyResult
{
    public string? ManifestPath { get; set; }
    public string? Target { get; set; }
    public bool Success { get; set; }
    public PowerForgeServerVerifyCommandResult[]? Commands { get; set; }
    public PowerForgeServerVerifyUrlResult[]? Urls { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerVerifyCommandResult
{
    public string? Id { get; set; }
    public string? Command { get; set; }
    public bool Required { get; set; }
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public string? OutputPreview { get; set; }
    public string? ErrorPreview { get; set; }
}

internal sealed class PowerForgeServerVerifyUrlResult
{
    public string? Url { get; set; }
    public int? ExpectedStatus { get; set; }
    public int? ActualStatus { get; set; }
    public string? Via { get; set; }
    public bool Success { get; set; }
    public string? ServerHeader { get; set; }
    public string? Error { get; set; }
}

internal sealed class PowerForgeServerDeployResult
{
    public string? ManifestPath { get; set; }
    public string? Target { get; set; }
    public bool DryRun { get; set; }
    public bool Success { get; set; }
    public PowerForgeServerDeployCommandResult[]? Commands { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerDeployCommandResult
{
    public string? Id { get; set; }
    public string? Command { get; set; }
    public string? WorkingDirectory { get; set; }
    public bool Required { get; set; }
    public bool Sensitive { get; set; }
    public int ExitCode { get; set; }
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public string? OutputPreview { get; set; }
    public string? ErrorPreview { get; set; }
}

internal sealed class PowerForgeServerBootstrapPlanResult
{
    public string? ManifestPath { get; set; }
    public string? OutputPath { get; set; }
    public string? MarkdownPath { get; set; }
    public string? ScriptPath { get; set; }
    public PowerForgeServerBootstrapPlanStep[]? Steps { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerBootstrapPlanStep
{
    public int Order { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Command { get; set; }
    public bool Manual { get; set; }
    public bool Sensitive { get; set; }
}

internal sealed class PowerForgeServerRestoreSecretsPlanResult
{
    public string? ManifestPath { get; set; }
    public string? OutputPath { get; set; }
    public string? MarkdownPath { get; set; }
    public string? ScriptPath { get; set; }
    public string? ArchivePath { get; set; }
    public string? Encryption { get; set; }
    public string? RecipientEnv { get; set; }
    public PowerForgeServerRestoreSecretEntry[]? Secrets { get; set; }
    public string[]? Warnings { get; set; }
}

internal sealed class PowerForgeServerRestoreSecretEntry
{
    public string Id { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Env { get; set; }
    public string? RestoreMode { get; set; }
    public string? RequiredFor { get; set; }
}

internal sealed class ProcessResult
{
    public int ExitCode { get; set; }
    public string Stdout { get; set; } = string.Empty;
    public string Stderr { get; set; } = string.Empty;
    public bool Success => ExitCode == 0;
}
