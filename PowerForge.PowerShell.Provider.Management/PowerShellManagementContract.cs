using System.Security;
using System.Text.Json.Serialization;
using Microsoft.Management.Infrastructure;

namespace PowerForge;

/// <summary>Generic management-provider operation implemented by the typed CIM/MI provider.</summary>
public enum PowerShellManagementOperation
{
    /// <summary>Execute a provider query.</summary>
    Query,
    /// <summary>Enumerate instances of one class.</summary>
    Enumerate,
    /// <summary>Get one exact instance by portable key reference.</summary>
    Get,
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

/// <summary>Runtime authentication mechanism. Credential values are never part of the portable contract.</summary>
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

/// <summary>Portable management adapter contract stored in provider metadata and locks.</summary>
public sealed class PowerShellManagementProviderContract
{
    /// <summary>Contract schema version.</summary>
    public int SchemaVersion { get; set; } = 2;
    /// <summary>Stable provider identity.</summary>
    public string ProviderId { get; set; } = string.Empty;
    /// <summary>Provider contract version.</summary>
    public string ProviderVersion { get; set; } = "2.0";
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
    public string Serialization { get; set; } = "PowerForge.Management/2";
    /// <summary>Error translation contract.</summary>
    public string Errors { get; set; } = "ProviderException";
    /// <summary>Default operation timeout in seconds.</summary>
    public int DefaultTimeoutSeconds { get; set; } = 120;
    /// <summary>Maximum default throttle.</summary>
    public int MaximumThrottle { get; set; } = 32;
}

/// <summary>Runtime-only credential used when the provider creates a session.</summary>
public sealed class PowerShellManagementCredential
{
    /// <summary>Creates a runtime credential without making its password serializable.</summary>
    public PowerShellManagementCredential(string userName, SecureString password, string domain = "")
    {
        UserName = string.IsNullOrWhiteSpace(userName) ? throw new ArgumentException("A credential user name is required.", nameof(userName)) : userName;
        Password = password ?? throw new ArgumentNullException(nameof(password));
        Domain = domain ?? string.Empty;
    }

    /// <summary>User name.</summary>
    public string UserName { get; }
    /// <summary>Optional domain.</summary>
    public string Domain { get; }
    /// <summary>Secure password value.</summary>
    [JsonIgnore]
    public SecureString Password { get; }
}

/// <summary>One portable management property value.</summary>
public sealed class PowerShellManagementProperty
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Normalized scalar value text retained for schema-1 compatibility.</summary>
    public string Value { get; set; } = string.Empty;
    /// <summary>Ordered normalized values for an array property.</summary>
    public string[] Values { get; set; } = Array.Empty<string>();
    /// <summary>CIM type identity.</summary>
    public string TypeName { get; set; } = "String";
    /// <summary>Whether the value is null.</summary>
    public bool IsNull { get; set; }
    /// <summary>Whether the property is an array.</summary>
    public bool IsArray { get; set; }
}

/// <summary>Portable instance reference reconstructed from exact CIM key values.</summary>
public sealed class PowerShellManagementInstanceReference
{
    /// <summary>Management class name.</summary>
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Management namespace.</summary>
    public string Namespace { get; set; } = string.Empty;
    /// <summary>Exact key properties.</summary>
    public PowerShellManagementProperty[] Keys { get; set; } = Array.Empty<PowerShellManagementProperty>();
}

/// <summary>One runtime management-provider operation.</summary>
public sealed class PowerShellManagementRequest
{
    /// <summary>Requested generic operation.</summary>
    public PowerShellManagementOperation Operation { get; set; }
    /// <summary>Target computer, or empty for the local computer.</summary>
    public string ComputerName { get; set; } = string.Empty;
    /// <summary>Management namespace.</summary>
    public string Namespace { get; set; } = "root/cimv2";
    /// <summary>Management class.</summary>
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Query dialect.</summary>
    public string QueryDialect { get; set; } = "WQL";
    /// <summary>Query expression or subscription expression.</summary>
    public string Query { get; set; } = string.Empty;
    /// <summary>Method name.</summary>
    public string MethodName { get; set; } = string.Empty;
    /// <summary>Association class name.</summary>
    public string AssociationClassName { get; set; } = string.Empty;
    /// <summary>Source role for association traversal.</summary>
    public string SourceRole { get; set; } = string.Empty;
    /// <summary>Result role for association traversal.</summary>
    public string ResultRole { get; set; } = string.Empty;
    /// <summary>Runtime transport.</summary>
    public PowerShellManagementTransport Transport { get; set; }
    /// <summary>Runtime authentication mechanism.</summary>
    public PowerShellManagementAuthentication Authentication { get; set; }
    /// <summary>Operation timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;
    /// <summary>Maximum subscription results returned by one bounded operation.</summary>
    public int MaximumResults { get; set; } = 1;
    /// <summary>Maximum query, enumeration, or association results; zero means unbounded.</summary>
    public int ResultLimit { get; set; }
    /// <summary>Portable property values used by create and modify.</summary>
    public PowerShellManagementProperty[] Properties { get; set; } = Array.Empty<PowerShellManagementProperty>();
    /// <summary>Portable method input values.</summary>
    public PowerShellManagementProperty[] MethodParameters { get; set; } = Array.Empty<PowerShellManagementProperty>();
    /// <summary>Portable target-instance identity used across process boundaries.</summary>
    public PowerShellManagementInstanceReference? InstanceReference { get; set; }
    /// <summary>Existing live CIM instance for same-process operation chaining.</summary>
    [JsonIgnore]
    public CimInstance? Instance { get; set; }
    /// <summary>Existing live CIM session. The adapter does not dispose caller-owned sessions.</summary>
    [JsonIgnore]
    public CimSession? Session { get; set; }
    /// <summary>Runtime-only credential. It is never copied into results, locks, or diagnostics.</summary>
    [JsonIgnore]
    public PowerShellManagementCredential? Credential { get; set; }
}

/// <summary>Portable management instance observation plus a runtime-only instance handle.</summary>
public sealed class PowerShellManagementInstance : IDisposable
{
    /// <summary>Management class name.</summary>
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Management namespace.</summary>
    public string Namespace { get; set; } = string.Empty;
    /// <summary>Server name reported by the provider.</summary>
    public string ServerName { get; set; } = string.Empty;
    /// <summary>Stable, sorted property observations.</summary>
    public PowerShellManagementProperty[] Properties { get; set; } = Array.Empty<PowerShellManagementProperty>();
    /// <summary>Portable instance identity when exact key properties were returned.</summary>
    public PowerShellManagementInstanceReference? Reference { get; set; }
    /// <summary>Runtime-only CIM instance used for a subsequent typed operation.</summary>
    [JsonIgnore]
    public CimInstance? RuntimeInstance { get; set; }

    /// <summary>Releases the runtime-only CIM instance while retaining portable observations.</summary>
    public void Dispose()
    {
        RuntimeInstance?.Dispose();
        RuntimeInstance = null;
    }
}

/// <summary>Portable result from one typed management-provider operation.</summary>
public sealed class PowerShellManagementResult : IDisposable
{
    /// <summary>Completed operation.</summary>
    public PowerShellManagementOperation Operation { get; set; }
    /// <summary>Returned instances.</summary>
    public PowerShellManagementInstance[] Instances { get; set; } = Array.Empty<PowerShellManagementInstance>();
    /// <summary>Method return value.</summary>
    public PowerShellManagementProperty? ReturnValue { get; set; }
    /// <summary>Method output parameters.</summary>
    public PowerShellManagementProperty[] OutputParameters { get; set; } = Array.Empty<PowerShellManagementProperty>();
    /// <summary>Whether the adapter created and deterministically disposed its session.</summary>
    public bool OwnedSessionDisposed { get; set; }

    /// <summary>Releases all runtime-only instance handles while retaining portable result data.</summary>
    public void Dispose()
    {
        foreach (var instance in Instances) instance.Dispose();
    }
}
