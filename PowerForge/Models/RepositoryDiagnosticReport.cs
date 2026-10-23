namespace PowerForge;

/// <summary>A secret-free description of a repository operation and its authentication inputs.</summary>
public sealed class RepositoryDiagnosticReport
{
    /// <summary>Version of the exported report contract.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Inspection time in UTC.</summary>
    public DateTimeOffset CollectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Operation being inspected; inspection alone performs no network operation.</summary>
    public string Operation { get; set; } = "Inspect";
    /// <summary>Inspection or BeforePublish; publish reports describe inputs before the backend runs.</summary>
    public string CollectionPhase { get; set; } = "Inspection";
    /// <summary>Selected publishing backend.</summary>
    public string Tool { get; set; } = string.Empty;
    /// <summary>Repository name, with sensitive text removed.</summary>
    public string RepositoryName { get; set; } = string.Empty;
    /// <summary>Configured endpoint, without URL credentials, query, or fragment.</summary>
    public string? ConfiguredUri { get; set; }
    /// <summary>Registered endpoint, without URL credentials, query, or fragment.</summary>
    public string? RegisteredUri { get; set; }
    /// <summary>Publish source passed to NuGet after native URL rewriting. A v3 index can resolve an upload endpoint later.</summary>
    public string? PublishSourceUri { get; set; }
    /// <summary>Whether the selected backend has this repository registered; null means registration could not be determined without changing settings.</summary>
    public bool? RepositoryFound { get; set; }
    /// <summary>Package or module path associated with the operation.</summary>
    public string? PackagePath { get; set; }
    /// <summary>True or false when the invocation was observed; null during standalone inspection.</summary>
    public bool? CredentialSupplied { get; set; }
    /// <summary>Non-secret origin of the supplied repository credential.</summary>
    public string? CredentialSource { get; set; }
    /// <summary>True or false when the invocation was observed; null during standalone inspection.</summary>
    public bool? ApiKeySupplied { get; set; }
    /// <summary>Non-secret origin of the explicit API key.</summary>
    public string? ApiKeySource { get; set; }
    /// <summary>Whether this operation asks PowerForge to persist a credential.</summary>
    public bool CredentialsPersistedByPowerForge { get; set; }
    /// <summary>What is known about backend selection; supplied credentials do not prove selection.</summary>
    public string AuthenticationSelection { get; set; } = "Not reported by backend";
    /// <summary>Actual metadata captured in the child process performing the operation.</summary>
    public RepositoryDiagnosticRuntime Runtime { get; set; } = new();
    /// <summary>Configuration files discovered by the backend, with inspection status.</summary>
    public RepositoryDiagnosticFile[] ConfigurationFiles { get; set; } = Array.Empty<RepositoryDiagnosticFile>();
    /// <summary>Relevant saved or inherited authentication inputs. Contains no credential values.</summary>
    public RepositoryDiagnosticInput[] AuthenticationInputs { get; set; } = Array.Empty<RepositoryDiagnosticInput>();
    /// <summary>Actionable findings and explicit inspection limitations.</summary>
    public RepositoryDiagnosticFinding[] Findings { get; set; } = Array.Empty<RepositoryDiagnosticFinding>();
}

/// <summary>Metadata from the process and module that inspect or publish to a repository.</summary>
public sealed class RepositoryDiagnosticRuntime
{
    /// <summary>PowerShell executable path.</summary>
    public string? Executable { get; set; }
    /// <summary>PowerShell version in the child process.</summary>
    public string? PowerShellVersion { get; set; }
    /// <summary>.NET runtime version in the child process.</summary>
    public string? DotNetVersion { get; set; }
    /// <summary>Working directory used by the child process.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>Actual imported publisher module version.</summary>
    public string? ModuleVersion { get; set; }
    /// <summary>Actual imported publisher module path.</summary>
    public string? ModulePath { get; set; }
    /// <summary>Whether the selected publisher module imported successfully.</summary>
    public bool ModuleAvailable { get; set; }
    /// <summary>NuGet.Configuration version loaded by that publisher, when exposed.</summary>
    public string? NuGetVersion { get; set; }
    /// <summary>Complete, Partial, Unavailable, or NotApplicable; never implies successful authentication.</summary>
    public string SettingsInspection { get; set; } = "Unavailable";
    /// <summary>Whether a provider is configured on the repository. Its cached tokens are not inspected.</summary>
    public bool CredentialProviderConfigured { get; set; }
    /// <summary>Whether native registration contains a reference to a stored credential. The vault is not opened.</summary>
    public bool CredentialReferenceConfigured { get; set; }
}

/// <summary>Presence and readability of a relevant configuration file.</summary>
public sealed class RepositoryDiagnosticFile
{
    /// <summary>Configuration file location, with the user profile replaced by a placeholder.</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>Readable, Missing, or Unreadable; unreadable files are not reported as absent.</summary>
    public string Status { get; set; } = string.Empty;
}

/// <summary>Non-secret metadata about a possible authentication input.</summary>
public sealed class RepositoryDiagnosticInput
{
    /// <summary>NuGetApiKey, SourceCredential, EnvironmentCredential, or ApiKeyEnvironment.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Source name, environment variable name, or sanitized API key endpoint.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Configuration file or process environment containing the input.</summary>
    public string Location { get; set; } = string.Empty;
    /// <summary>Whether the input exists; null means inspection was not possible.</summary>
    public bool? Present { get; set; }
    /// <summary>Whether the saved entry remains in the backend's merged settings. Null means not determined.</summary>
    public bool? Effective { get; set; }
    /// <summary>ExactEndpoint, SameHost, or SourceName. A host match alone does not prove applicability.</summary>
    public string Match { get; set; } = string.Empty;
    /// <summary>Encrypted, ClearText, EnvironmentReference, or ProcessEnvironment; values are never exported.</summary>
    public string Storage { get; set; } = string.Empty;
}

/// <summary>An explanation and next step associated with repository inspection.</summary>
public sealed class RepositoryDiagnosticFinding
{
    /// <summary>Stable diagnostic identifier.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Info, Warning, or Error.</summary>
    public string Severity { get; set; } = "Info";
    /// <summary>Human-readable explanation that contains no secrets.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Suggested investigation or remediation.</summary>
    public string RecommendedAction { get; set; } = string.Empty;
}

/// <summary>Secret-free inputs for read-only repository inspection.</summary>
public sealed class RepositoryDiagnosticRequest
{
    /// <summary>Repository to inspect. Defaults to the publisher's usual public repository.</summary>
    public string RepositoryName { get; set; } = "PSGallery";
    /// <summary>Backend whose environment and registrations should be inspected.</summary>
    public PublishTool Tool { get; set; } = PublishTool.PSResourceGet;
    /// <summary>Optional expected endpoint for comparison with the registered repository.</summary>
    public string? RepositoryUri { get; set; }
    /// <summary>Optional working directory for the inspection child process.</summary>
    public string? WorkingDirectory { get; set; }
}
