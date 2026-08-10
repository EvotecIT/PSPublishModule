namespace PowerForge.Web;

/// <summary>Severity emitted by the provider capability doctor.</summary>
public enum WebSearchProviderCheckSeverity
{
    /// <summary>Informational provider state.</summary>
    Info,
    /// <summary>Non-blocking readiness concern.</summary>
    Warning,
    /// <summary>Configuration defect that blocks safe collection.</summary>
    Error
}

/// <summary>One provider capability doctor finding.</summary>
public sealed class WebSearchProviderCheck
{
    /// <summary>Stable machine-readable check code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Finding severity.</summary>
    public WebSearchProviderCheckSeverity Severity { get; set; }

    /// <summary>Optional fleet site identifier.</summary>
    public string? SiteId { get; set; }

    /// <summary>Optional provider registration identifier.</summary>
    public string? ProviderId { get; set; }

    /// <summary>Human-readable finding.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Action that can make the provider ready.</summary>
    public string? Remediation { get; set; }
}

/// <summary>Resolved capability state for one provider registration.</summary>
public sealed class WebSearchProviderCapabilityState
{
    /// <summary>Fleet site identifier.</summary>
    public string SiteId { get; set; } = string.Empty;

    /// <summary>Provider registration identifier.</summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>Provider adapter kind.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Whether collection is enabled in configuration.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the provider registration and credential reference are valid.</summary>
    public bool ConfigurationReady { get; set; }

    /// <summary>Whether the current executable contains the authenticated collector.</summary>
    public bool CollectorAvailable { get; set; }

    /// <summary>Whether collection can run now.</summary>
    public bool CollectionReady => Enabled && ConfigurationReady && CollectorAvailable;

    /// <summary>Requested capabilities.</summary>
    public string[] RequestedCapabilities { get; set; } = Array.Empty<string>();

    /// <summary>Capabilities known for the provider kind.</summary>
    public string[] SupportedCapabilities { get; set; } = Array.Empty<string>();

    /// <summary>Requested capabilities implemented by the current executable.</summary>
    public string[] AvailableCollectorCapabilities { get; set; } = Array.Empty<string>();

    /// <summary>Requested capabilities not implemented by the current executable.</summary>
    public string[] MissingCollectorCapabilities { get; set; } = Array.Empty<string>();
}

/// <summary>Fleet provider configuration and capability doctor result.</summary>
public sealed class WebSearchProviderDoctorResult
{
    /// <summary>Whether the configuration has no blocking errors.</summary>
    public bool Success { get; set; }

    /// <summary>Deterministic identity of a valid non-secret fleet provider configuration.</summary>
    public string? ConfigurationHash { get; set; }

    /// <summary>Number of configured sites.</summary>
    public int SiteCount { get; set; }

    /// <summary>Number of configured provider registrations.</summary>
    public int ProviderCount { get; set; }

    /// <summary>Number of providers whose configuration and credential reference are ready.</summary>
    public int ConfigurationReadyCount { get; set; }

    /// <summary>Number of providers with a collector available in this executable.</summary>
    public int CollectorAvailableCount { get; set; }

    /// <summary>Resolved state for each registration.</summary>
    public WebSearchProviderCapabilityState[] Providers { get; set; } = Array.Empty<WebSearchProviderCapabilityState>();

    /// <summary>Ordered doctor findings.</summary>
    public WebSearchProviderCheck[] Checks { get; set; } = Array.Empty<WebSearchProviderCheck>();
}
