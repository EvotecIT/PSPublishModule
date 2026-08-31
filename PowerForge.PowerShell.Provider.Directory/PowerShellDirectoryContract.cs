using System.DirectoryServices.Protocols;
using System.Security;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Generic LDAP operation implemented by the typed directory provider.</summary>
public enum PowerShellDirectoryOperation
{
    /// <summary>Search a directory subtree using an LDAP filter.</summary>
    Search,
    /// <summary>Read one exact distinguished name.</summary>
    Read,
    /// <summary>Add one directory entry.</summary>
    Add,
    /// <summary>Modify attributes on one directory entry.</summary>
    Modify,
    /// <summary>Delete one directory entry.</summary>
    Delete,
    /// <summary>Rename or move one directory entry.</summary>
    ModifyDistinguishedName,
    /// <summary>Compare one attribute value without returning the entry.</summary>
    Compare
}

/// <summary>LDAP transport security mode.</summary>
public enum PowerShellDirectoryTransport
{
    /// <summary>Plain LDAP transport. Basic authentication is rejected.</summary>
    Ldap,
    /// <summary>LDAP protected by TLS from connection establishment. Reserved until a target profile qualifies it.</summary>
    Ldaps,
    /// <summary>LDAP upgraded with the StartTLS extended operation before binding. Reserved until a target profile qualifies it.</summary>
    StartTls
}

/// <summary>LDAP authentication selected at runtime.</summary>
public enum PowerShellDirectoryAuthentication
{
    /// <summary>Negotiate the strongest mutually supported integrated mechanism.</summary>
    Negotiate,
    /// <summary>Kerberos authentication. Reserved until a target profile qualifies it.</summary>
    Kerberos,
    /// <summary>NTLM authentication. Reserved until a target profile qualifies it.</summary>
    Ntlm,
    /// <summary>Basic authentication over TLS. Reserved until a target profile qualifies it.</summary>
    Basic,
    /// <summary>Anonymous bind. Reserved until a target profile qualifies it.</summary>
    Anonymous
}

/// <summary>LDAP search scope.</summary>
public enum PowerShellDirectorySearchScope
{
    /// <summary>Only the requested base entry.</summary>
    Base,
    /// <summary>Immediate children of the base entry.</summary>
    OneLevel,
    /// <summary>The complete subtree rooted at the base entry.</summary>
    Subtree
}

/// <summary>LDAP attribute modification behavior.</summary>
public enum PowerShellDirectoryModificationOperation
{
    /// <summary>Add values to the attribute.</summary>
    Add,
    /// <summary>Delete the attribute or selected values.</summary>
    Delete,
    /// <summary>Replace all values.</summary>
    Replace
}

/// <summary>Portable directory-provider contract.</summary>
public sealed class PowerShellDirectoryProviderContract
{
    /// <summary>Contract schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Stable provider identity.</summary>
    public string ProviderId { get; set; } = "powerforge.directory.ldap";
    /// <summary>Provider contract version.</summary>
    public string ProviderVersion { get; set; } = "1.0";
    /// <summary>Supported operations.</summary>
    public PowerShellDirectoryOperation[] Operations { get; set; } = Array.Empty<PowerShellDirectoryOperation>();
    /// <summary>Supported transports.</summary>
    public PowerShellDirectoryTransport[] Transports { get; set; } = Array.Empty<PowerShellDirectoryTransport>();
    /// <summary>Supported authentication mechanisms.</summary>
    public PowerShellDirectoryAuthentication[] Authentication { get; set; } = Array.Empty<PowerShellDirectoryAuthentication>();
    /// <summary>Whether a provider-created reusable session may be supplied at runtime.</summary>
    public bool AcceptsRuntimeSession { get; set; }
    /// <summary>Whether cancellation is observed after the platform-owned initial bind has completed.</summary>
    public bool SupportsCancellationAfterInitialization { get; set; }
    /// <summary>Whether adapter-owned connections are deterministically released.</summary>
    public bool DeterministicCleanup { get; set; }
    /// <summary>Portable result serialization identity.</summary>
    public string Serialization { get; set; } = "PowerForge.Directory/1";
}

/// <summary>Runtime-only LDAP credential.</summary>
public sealed class PowerShellDirectoryCredential
{
    /// <summary>Creates a credential whose password is excluded from portable serialization.</summary>
    public PowerShellDirectoryCredential(string userName, SecureString password, string domain = "")
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

/// <summary>A provider-created, profile-bound LDAP session that may be reused across typed operations.</summary>
public sealed class PowerShellDirectorySession : IDisposable
{
    private bool _disposed;

    internal PowerShellDirectorySession(LdapConnection connection)
        => _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    internal LdapConnection Connection
        => !_disposed ? _connection : throw new ObjectDisposedException(nameof(PowerShellDirectorySession));

    private readonly LdapConnection _connection;

    /// <summary>Releases the LDAP connection owned by this session.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
    }
}

/// <summary>One portable LDAP value. Binary bytes use Base64; text is optional convenience data.</summary>
public sealed class PowerShellDirectoryValue
{
    /// <summary>Text value when authored or losslessly decoded as UTF-8.</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Base64-encoded exact bytes for binary data.</summary>
    public string Base64 { get; set; } = string.Empty;
    /// <summary>Whether Base64 is the authoritative representation.</summary>
    public bool IsBinary { get; set; }
}

/// <summary>One LDAP attribute and its ordered values.</summary>
public sealed class PowerShellDirectoryAttribute
{
    /// <summary>LDAP attribute name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Attribute values.</summary>
    public PowerShellDirectoryValue[] Values { get; set; } = Array.Empty<PowerShellDirectoryValue>();
}

/// <summary>One LDAP modification.</summary>
public sealed class PowerShellDirectoryModification
{
    /// <summary>LDAP attribute name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Modification behavior.</summary>
    public PowerShellDirectoryModificationOperation Operation { get; set; }
    /// <summary>Values used by the modification.</summary>
    public PowerShellDirectoryValue[] Values { get; set; } = Array.Empty<PowerShellDirectoryValue>();
}

/// <summary>One typed LDAP operation request.</summary>
public sealed class PowerShellDirectoryRequest
{
    /// <summary>Requested operation.</summary>
    public PowerShellDirectoryOperation Operation { get; set; }
    /// <summary>LDAP server DNS name.</summary>
    public string HostName { get; set; } = string.Empty;
    /// <summary>Server port; zero selects 389 or 636 from the transport.</summary>
    public int Port { get; set; }
    /// <summary>Search base distinguished name. Empty is valid for RootDSE reads.</summary>
    public string BaseDistinguishedName { get; set; } = string.Empty;
    /// <summary>Exact target distinguished name for non-search operations.</summary>
    public string DistinguishedName { get; set; } = string.Empty;
    /// <summary>LDAP search filter.</summary>
    public string Filter { get; set; } = "(objectClass=*)";
    /// <summary>Search scope.</summary>
    public PowerShellDirectorySearchScope Scope { get; set; } = PowerShellDirectorySearchScope.Subtree;
    /// <summary>Requested attribute names. Empty requests server defaults.</summary>
    public string[] AttributeNames { get; set; } = Array.Empty<string>();
    /// <summary>Attributes used to add an entry.</summary>
    public PowerShellDirectoryAttribute[] Attributes { get; set; } = Array.Empty<PowerShellDirectoryAttribute>();
    /// <summary>Attribute changes used by modify.</summary>
    public PowerShellDirectoryModification[] Modifications { get; set; } = Array.Empty<PowerShellDirectoryModification>();
    /// <summary>New relative distinguished name for rename/move.</summary>
    public string NewRelativeDistinguishedName { get; set; } = string.Empty;
    /// <summary>Optional new parent distinguished name for move.</summary>
    public string NewParentDistinguishedName { get; set; } = string.Empty;
    /// <summary>Whether the old relative distinguished name value is deleted.</summary>
    public bool DeleteOldRelativeDistinguishedName { get; set; } = true;
    /// <summary>Attribute name used by compare.</summary>
    public string CompareAttributeName { get; set; } = string.Empty;
    /// <summary>Value used by compare.</summary>
    public PowerShellDirectoryValue? CompareValue { get; set; }
    /// <summary>Transport security mode.</summary>
    public PowerShellDirectoryTransport Transport { get; set; }
    /// <summary>Authentication mechanism.</summary>
    public PowerShellDirectoryAuthentication Authentication { get; set; }
    /// <summary>Operation timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
    /// <summary>LDAP page size; zero disables paging.</summary>
    public int PageSize { get; set; } = 500;
    /// <summary>Maximum returned entries.</summary>
    public int ResultLimit { get; set; } = 1000;
    /// <summary>Whether referrals may be followed.</summary>
    public bool FollowReferrals { get; set; }
    /// <summary>Provider-created runtime session for same-process operation chaining.</summary>
    [JsonIgnore]
    public PowerShellDirectorySession? Session { get; set; }
    /// <summary>Runtime-only credential. It never enters JSON, locks, or diagnostics.</summary>
    [JsonIgnore]
    public PowerShellDirectoryCredential? Credential { get; set; }
}

/// <summary>One portable LDAP entry.</summary>
public sealed class PowerShellDirectoryEntry
{
    /// <summary>Entry distinguished name.</summary>
    public string DistinguishedName { get; set; } = string.Empty;
    /// <summary>Sorted attribute observations.</summary>
    public PowerShellDirectoryAttribute[] Attributes { get; set; } = Array.Empty<PowerShellDirectoryAttribute>();
}

/// <summary>Portable result from one LDAP operation.</summary>
public sealed class PowerShellDirectoryResult
{
    /// <summary>Completed operation.</summary>
    public PowerShellDirectoryOperation Operation { get; set; }
    /// <summary>LDAP result-code name.</summary>
    public string ResultCode { get; set; } = string.Empty;
    /// <summary>Matched distinguished name returned by the server.</summary>
    public string MatchedDistinguishedName { get; set; } = string.Empty;
    /// <summary>Server diagnostic message without credential data.</summary>
    public string DiagnosticMessage { get; set; } = string.Empty;
    /// <summary>Returned directory entries.</summary>
    public PowerShellDirectoryEntry[] Entries { get; set; } = Array.Empty<PowerShellDirectoryEntry>();
    /// <summary>Compare outcome when the operation is Compare.</summary>
    public bool? Compared { get; set; }
    /// <summary>Whether the adapter created and disposed its connection.</summary>
    public bool OwnedConnectionDisposed { get; set; }
    /// <summary>Whether an outstanding server paging cookie was explicitly abandoned after an early stop.</summary>
    public bool PagingCookieAbandoned { get; set; }
    /// <summary>Whether the connection was closed to release paging state after the server rejected protocol abandonment.</summary>
    public bool PagingConnectionDisposed { get; set; }
}
