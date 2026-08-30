using System;

namespace PowerForge;

/// <summary>Generic management-provider operation understood by the PowerForge adapter contract.</summary>
public enum PowerShellManagementOperation
{
    /// <summary>Execute a provider query.</summary>
    Query,
    /// <summary>Enumerate instances of one class.</summary>
    Enumerate,
    /// <summary>Create one instance.</summary>
    Create,
    /// <summary>Modify one instance.</summary>
    Modify,
    /// <summary>Delete one instance.</summary>
    Delete,
    /// <summary>Invoke one provider method.</summary>
    InvokeMethod,
    /// <summary>Enumerate associated instances.</summary>
    Association,
    /// <summary>Subscribe to provider indications.</summary>
    Subscription
}

/// <summary>Management transport selected at runtime.</summary>
public enum PowerShellManagementTransport
{
    /// <summary>Provider default for the target and host.</summary>
    Default,
    /// <summary>WS-Management transport.</summary>
    WsMan,
    /// <summary>Windows DCOM transport.</summary>
    Dcom
}

/// <summary>Runtime authentication mechanism. Credential values are never part of this portable contract.</summary>
public enum PowerShellManagementAuthentication
{
    /// <summary>Provider default.</summary>
    Default,
    /// <summary>Negotiate the strongest mutually supported mechanism.</summary>
    Negotiate,
    /// <summary>Kerberos authentication.</summary>
    Kerberos,
    /// <summary>NTLM domain authentication.</summary>
    NtlmDomain,
    /// <summary>Basic authentication over a separately secured transport.</summary>
    Basic,
    /// <summary>CredSSP delegated authentication.</summary>
    CredSsp
}

/// <summary>
/// Portable management adapter contract stored in provider metadata and locks.
/// It describes behavior but never contains a credential or live session identity.
/// </summary>
public sealed class PowerShellManagementProviderContract
{
    /// <summary>Contract schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable provider identity.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider contract version.</summary>
    public string ProviderVersion { get; set; } = "1.0";

    /// <summary>Supported management operations.</summary>
    public PowerShellManagementOperation[] Operations { get; set; } = Array.Empty<PowerShellManagementOperation>();

    /// <summary>Supported transports.</summary>
    public PowerShellManagementTransport[] Transports { get; set; } = Array.Empty<PowerShellManagementTransport>();

    /// <summary>Supported authentication mechanisms.</summary>
    public PowerShellManagementAuthentication[] Authentication { get; set; } = Array.Empty<PowerShellManagementAuthentication>();

    /// <summary>Whether the runtime adapter accepts an existing session supplied at runtime.</summary>
    public bool AcceptsRuntimeSession { get; set; }

    /// <summary>Whether cancellation is propagated to provider work.</summary>
    public bool SupportsCancellation { get; set; }

    /// <summary>Whether disconnect and disposal are deterministic.</summary>
    public bool DeterministicCleanup { get; set; }

    /// <summary>Portable result serialization contract.</summary>
    public string Serialization { get; set; } = "PowerForge.Management/1";

    /// <summary>Error translation contract.</summary>
    public string Errors { get; set; } = "ProviderException";

    /// <summary>Default operation timeout in seconds.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum default throttle.</summary>
    public int MaximumThrottle { get; set; } = 32;
}

/// <summary>One deterministic command shape read from CDXML metadata.</summary>
public sealed class PowerShellCdxmlCommand
{
    /// <summary>PowerShell command name.</summary>
    public string CommandName { get; set; } = string.Empty;

    /// <summary>Underlying management method or query role.</summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Declared parameters ordered by metadata identity.</summary>
    public string[] Parameters { get; set; } = Array.Empty<string>();
}

/// <summary>Deterministic CDXML metadata parsed without module import or management-target access.</summary>
public sealed class PowerShellCdxmlMetadata
{
    /// <summary>Metadata schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>CDXML schema URI.</summary>
    public string SchemaUri { get; set; } = string.Empty;

    /// <summary>Management class name.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Class version.</summary>
    public string ClassVersion { get; set; } = string.Empty;

    /// <summary>Default PowerShell noun.</summary>
    public string DefaultNoun { get; set; } = string.Empty;

    /// <summary>Declared commands.</summary>
    public PowerShellCdxmlCommand[] Commands { get; set; } = Array.Empty<PowerShellCdxmlCommand>();

    /// <summary>SHA-256 of the exact CDXML input.</summary>
    public string SourceSha256 { get; set; } = string.Empty;
}
