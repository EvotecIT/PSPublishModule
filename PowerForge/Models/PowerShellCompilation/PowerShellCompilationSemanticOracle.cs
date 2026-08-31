using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PowerForge;

/// <summary>PowerShell host family used as semantic evidence for a compiler profile.</summary>
public enum PowerShellCompilationSemanticHostFamily
{
    /// <summary>Windows PowerShell 5.1 on the full .NET Framework.</summary>
    WindowsPowerShell51,

    /// <summary>Cross-platform PowerShell 7.</summary>
    PowerShell7
}

/// <summary>Recognized execution lanes accepted by semantic comparison and promotion.</summary>
public enum PowerShellCompilationSemanticExecutionSurface
{
    /// <summary>Source executed by the selected external PowerShell host.</summary>
    Interpreted,

    /// <summary>A generated Hybrid artifact executed by its selected PowerShell host.</summary>
    Hybrid,

    /// <summary>A runtime-free Strict CLR artifact.</summary>
    Strict,

    /// <summary>An equivalent hand-written runtime-free CLR implementation.</summary>
    HandWrittenClr
}

/// <summary>
/// Immutable identity of one supported interpreted PowerShell semantic oracle.
/// The compiler consumes this as evidence and never loads implementation assemblies from the host.
/// </summary>
public sealed class PowerShellCompilationSemanticOracleProfile
{
    /// <summary>Creates an immutable semantic-oracle profile.</summary>
    public PowerShellCompilationSemanticOracleProfile(
        string profileId,
        PowerShellCompilationSemanticHostFamily family,
        string hostExecutable,
        string powerShellEdition,
        string versionRange,
        string operatingSystem,
        string architecture,
        string culture,
        IEnumerable<string> featureSwitches,
        string upstreamRepository,
        string upstreamCommit,
        string documentationUri)
    {
        ProfileId = Require(profileId, nameof(profileId));
        Family = family;
        HostExecutable = Require(hostExecutable, nameof(hostExecutable));
        PowerShellEdition = Require(powerShellEdition, nameof(powerShellEdition));
        VersionRange = Require(versionRange, nameof(versionRange));
        PowerShellMajorVersion = ParseMajorVersion(VersionRange);
        OperatingSystem = Require(operatingSystem, nameof(operatingSystem));
        Architecture = Require(architecture, nameof(architecture));
        Culture = Require(culture, nameof(culture));
        FeatureSwitches = new ReadOnlyCollection<string>((featureSwitches ?? throw new ArgumentNullException(nameof(featureSwitches)))
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray());
        UpstreamRepository = Require(upstreamRepository, nameof(upstreamRepository));
        UpstreamCommit = upstreamCommit?.Trim() ?? string.Empty;
        DocumentationUri = Require(documentationUri, nameof(documentationUri));
    }

    /// <summary>Stable profile identity.</summary>
    public string ProfileId { get; }

    /// <summary>PowerShell host family.</summary>
    public PowerShellCompilationSemanticHostFamily Family { get; }

    /// <summary>Expected host executable.</summary>
    public string HostExecutable { get; }

    /// <summary>Expected value of <c>$PSVersionTable.PSEdition</c>.</summary>
    public string PowerShellEdition { get; }

    /// <summary>Supported host-version range.</summary>
    public string VersionRange { get; }

    /// <summary>PowerShell major version fixed by this semantic profile, or zero when a custom range cannot be reduced safely to one major.</summary>
    public int PowerShellMajorVersion { get; }

    /// <summary>Supported operating-system family.</summary>
    public string OperatingSystem { get; }

    /// <summary>Supported processor architecture.</summary>
    public string Architecture { get; }

    /// <summary>Culture used by the oracle observation.</summary>
    public string Culture { get; }

    /// <summary>Explicit semantic feature switches.</summary>
    public IReadOnlyList<string> FeatureSwitches { get; }

    /// <summary>Authoritative upstream source repository or product source identity.</summary>
    public string UpstreamRepository { get; }

    /// <summary>Pinned upstream source commit when source is publicly available.</summary>
    public string UpstreamCommit { get; }

    /// <summary>Authoritative compatibility documentation.</summary>
    public string DocumentationUri { get; }

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();

    private static int ParseMajorVersion(string versionRange)
    {
        if (versionRange.Length < 5 || versionRange[0] != '[' ||
            versionRange[versionRange.Length - 1] is not ')' and not ']')
            return 0;
        var separator = versionRange.IndexOf(',');
        if (separator <= 1 || separator >= versionRange.Length - 2 ||
            !Version.TryParse(versionRange.Substring(1, separator - 1).Trim(), out var lower) ||
            !Version.TryParse(versionRange.Substring(separator + 1, versionRange.Length - separator - 2).Trim(), out var upper) ||
            lower.Major <= 0 || upper.CompareTo(lower) <= 0)
            return 0;
        if (upper.Major == lower.Major)
            return lower.Major;
        var exclusiveNextMajorBoundary = versionRange[versionRange.Length - 1] == ')' &&
                                         upper.Major == lower.Major + 1 &&
                                         upper.Minor == 0 && upper.Build <= 0 && upper.Revision <= 0;
        return exclusiveNextMajorBoundary ? lower.Major : 0;
    }
}

/// <summary>Authoritative evidence used to implement one feature in one semantic profile.</summary>
public sealed class PowerShellCompilationSemanticFeatureProvenance
{
    /// <summary>Creates immutable per-feature provenance without linked minimized cases.</summary>
    public PowerShellCompilationSemanticFeatureProvenance(
        string featureId,
        string profileId,
        string upstreamCommit,
        IEnumerable<string> upstreamTests,
        IEnumerable<string> documentationUris,
        string expectedVersionDifference = "",
        string contractVersion = "1.0",
        string owningComponent = "PowerForge.SemanticPipeline")
        : this(
            featureId,
            profileId,
            upstreamCommit,
            (upstreamTests ?? throw new ArgumentNullException(nameof(upstreamTests))).ToArray(),
            (documentationUris ?? throw new ArgumentNullException(nameof(documentationUris))).ToArray(),
            Array.Empty<string>(),
            expectedVersionDifference,
            contractVersion,
            owningComponent)
    {
    }

    /// <summary>Creates immutable per-feature provenance.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public PowerShellCompilationSemanticFeatureProvenance(
        string featureId,
        string profileId,
        string upstreamCommit,
        IReadOnlyList<string> upstreamTests,
        IReadOnlyList<string> documentationUris,
        IReadOnlyList<string> caseIds,
        string expectedVersionDifference = "",
        string contractVersion = "1.0",
        string owningComponent = "PowerForge.SemanticPipeline")
    {
        FeatureId = Require(featureId, nameof(featureId));
        ProfileId = Require(profileId, nameof(profileId));
        UpstreamCommit = upstreamCommit?.Trim() ?? string.Empty;
        UpstreamTests = Normalize(upstreamTests, nameof(upstreamTests));
        DocumentationUris = Normalize(documentationUris, nameof(documentationUris));
        CaseIds = Normalize(caseIds ?? Array.Empty<string>(), nameof(caseIds));
        ExpectedVersionDifference = expectedVersionDifference?.Trim() ?? string.Empty;
        ContractVersion = Require(contractVersion, nameof(contractVersion));
        OwningComponent = Require(owningComponent, nameof(owningComponent));
    }

    /// <summary>Stable compiler feature identity.</summary>
    public string FeatureId { get; }

    /// <summary>Semantic profile to which the evidence applies.</summary>
    public string ProfileId { get; }

    /// <summary>Pinned upstream source commit when available.</summary>
    public string UpstreamCommit { get; }

    /// <summary>Repository-relative upstream test paths.</summary>
    public IReadOnlyList<string> UpstreamTests { get; }

    /// <summary>Authoritative documentation references.</summary>
    public IReadOnlyList<string> DocumentationUris { get; }

    /// <summary>Minimized executable cases that directly exercise this feature/profile contract.</summary>
    public IReadOnlyList<string> CaseIds { get; }

    /// <summary>Named and justified host-version difference, or empty when no difference is expected.</summary>
    public string ExpectedVersionDifference { get; }

    /// <summary>Version of the PowerForge semantic contract supported by this evidence.</summary>
    public string ContractVersion { get; }

    /// <summary>Canonical binder, IR, or runtime-free helper that owns the behavior.</summary>
    public string OwningComponent { get; }

    private static IReadOnlyList<string> Normalize(IEnumerable<string> values, string parameterName)
        => new ReadOnlyCollection<string>((values ?? throw new ArgumentNullException(parameterName))
            .Select(static value => value?.Trim() ?? string.Empty)
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray());

    private static string Require(string value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameterName)
            : value.Trim();
}

/// <summary>One pinned profile whose upstream source identity changed and therefore requires review.</summary>
public sealed class PowerShellCompilationSemanticUpstreamChange
{
    /// <summary>Creates an immutable upstream-change review proposal.</summary>
    public PowerShellCompilationSemanticUpstreamChange(
        string profileId,
        string pinnedCommit,
        string observedCommit,
        IEnumerable<string> affectedFeatureIds)
    {
        ProfileId = profileId ?? string.Empty;
        PinnedCommit = pinnedCommit ?? string.Empty;
        ObservedCommit = observedCommit ?? string.Empty;
        AffectedFeatureIds = new ReadOnlyCollection<string>((affectedFeatureIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static feature => feature, StringComparer.Ordinal)
            .ToArray());
    }

    /// <summary>Profile requiring review.</summary>
    public string ProfileId { get; }
    /// <summary>Immutable commit currently accepted by the semantic profile.</summary>
    public string PinnedCommit { get; }
    /// <summary>Newly observed upstream commit; it is never adopted automatically.</summary>
    public string ObservedCommit { get; }
    /// <summary>Promoted feature contracts that reference the changed profile.</summary>
    public IReadOnlyList<string> AffectedFeatureIds { get; }
}

/// <summary>One property captured from semantic output without retaining live runtime objects.</summary>
public sealed class PowerShellCompilationSemanticPropertyObservation
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized value text.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Runtime type identity, or empty for null.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether the property value is null.</summary>
    public bool IsNull { get; set; }

    /// <summary>Whether the property value is PowerShell's no-value sentinel.</summary>
    public bool IsAutomationNull { get; set; }

    /// <summary>Semantic value state: Value, Null, or AutomationNull.</summary>
    public string ValueState { get; set; } = "Value";

    /// <summary>Whether the value is scalar, a dictionary, or a collection retained as one value.</summary>
    public string EnumerationState { get; set; } = "Scalar";

    /// <summary>Known retained collection cardinality, or null when the value is not a countable collection.</summary>
    public int? CollectionCardinality { get; set; }

    /// <summary>Ordered distinct element runtime types for a safely inspectable retained collection.</summary>
    public string[] ElementTypeNames { get; set; } = Array.Empty<string>();
}

/// <summary>One normalized value written to a PowerShell stream.</summary>
public sealed class PowerShellCompilationSemanticValueObservation
{
    /// <summary>Cross-stream sequence assigned at the observation boundary.</summary>
    public int Sequence { get; set; }

    /// <summary>Normalized value text.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Runtime type identity, or empty for null or no output.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether the observed value is null.</summary>
    public bool IsNull { get; set; }

    /// <summary>Whether the observed value is PowerShell's internal no-value sentinel.</summary>
    public bool IsAutomationNull { get; set; }

    /// <summary>Semantic value state: Value, Null, or AutomationNull.</summary>
    public string ValueState { get; set; } = "Value";

    /// <summary>Whether the pipeline item is scalar, a dictionary, or a collection retained as one value.</summary>
    public string EnumerationState { get; set; } = "Scalar";

    /// <summary>Known retained collection cardinality, or null when the value is not a countable collection.</summary>
    public int? CollectionCardinality { get; set; }

    /// <summary>Ordered distinct element runtime types for a safely inspectable retained collection.</summary>
    public string[] ElementTypeNames { get; set; } = Array.Empty<string>();

    /// <summary>Stable selected-property snapshot in the source object's original property order.</summary>
    public PowerShellCompilationSemanticPropertyObservation[] Properties { get; set; } = Array.Empty<PowerShellCompilationSemanticPropertyObservation>();
}

/// <summary>
/// Portable black-box observation used to compare interpreted, Strict, Hybrid, and hand-written CLR execution.
/// It records semantic effects without serializing credentials, sessions, handles, or live provider objects.
/// </summary>
public sealed class PowerShellCompilationSemanticOracleEnvelope
{
    /// <summary>Envelope schema version.</summary>
    public int SchemaVersion { get; set; } = 3;

    /// <summary>Semantic profile identity.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Execution surface: Interpreted, Strict, Hybrid, or HandWrittenClr.</summary>
    public string ExecutionSurface { get; set; } = string.Empty;

    /// <summary>Exact host version when a PowerShell host participated.</summary>
    public string HostVersion { get; set; } = string.Empty;

    /// <summary>PowerShell edition when a PowerShell host participated.</summary>
    public string PowerShellEdition { get; set; } = string.Empty;

    /// <summary>Operating-system family.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Processor architecture.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Culture used during execution.</summary>
    public string Culture { get; set; } = string.Empty;

    /// <summary>Integrity-bound exact executable/runtime identity for host-backed observations.</summary>
    public PowerShellCompilationSemanticHostArtifact? HostArtifact { get; set; }

    /// <summary>Ordered success-stream observations.</summary>
    public PowerShellCompilationSemanticValueObservation[] Success { get; set; } = Array.Empty<PowerShellCompilationSemanticValueObservation>();

    /// <summary>Observable success result state: Output or NoOutput.</summary>
    public string SuccessState { get; set; } = "NoOutput";

    /// <summary>Ordered information-stream observations.</summary>
    public string[] Information { get; set; } = Array.Empty<string>();

    /// <summary>Ordered warning-stream observations.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();

    /// <summary>Ordered verbose-stream observations.</summary>
    public string[] Verbose { get; set; } = Array.Empty<string>();

    /// <summary>Ordered debug-stream observations.</summary>
    public string[] Debug { get; set; } = Array.Empty<string>();

    /// <summary>Ordered structured information, warning, verbose, and debug records.</summary>
    public PowerShellCompilationSemanticStreamObservation[] StreamRecords { get; set; } = Array.Empty<PowerShellCompilationSemanticStreamObservation>();

    /// <summary>Ordered normalized error identities.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();

    /// <summary>Ordered structured error records including identity, category, exception, target, and termination.</summary>
    public PowerShellCompilationSemanticErrorObservation[] ErrorRecords { get; set; } = Array.Empty<PowerShellCompilationSemanticErrorObservation>();

    /// <summary>Process-style exit code when applicable.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Whether execution produced no success output.</summary>
    public bool NoSuccessOutput => Success.Length == 0;

    /// <summary>Number of success-stream values after PowerShell enumeration semantics.</summary>
    public int SuccessCardinality => Success.Length;

    /// <summary>Normalized file-system effects relative to the isolated oracle root.</summary>
    public string[] FileSystemEffects { get; set; } = Array.Empty<string>();

    /// <summary>Encoding facts that can affect text and native-command behavior.</summary>
    public PowerShellCompilationSemanticEncodingObservation Encoding { get; set; } = new();

    /// <summary>Final process-related state exposed by the execution surface.</summary>
    public PowerShellCompilationSemanticProcessStateObservation ProcessState { get; set; } = new();

    /// <summary>Directly observed, ordered child-process launch and exit effects.</summary>
    public PowerShellCompilationSemanticProcessEffectObservation[] ProcessEffects { get; set; } = Array.Empty<PowerShellCompilationSemanticProcessEffectObservation>();
}

/// <summary>Portable encoding facts recorded at an oracle execution boundary.</summary>
public sealed class PowerShellCompilationSemanticEncodingObservation
{
    /// <summary>Console input encoding web name.</summary>
    public string ConsoleInput { get; set; } = string.Empty;

    /// <summary>Console output encoding web name.</summary>
    public string ConsoleOutput { get; set; } = string.Empty;

    /// <summary>PowerShell native-pipeline output encoding web name when applicable.</summary>
    public string PowerShellOutput { get; set; } = string.Empty;

    /// <summary>Encoding used for the observation artifact itself.</summary>
    public string ObservationFile { get; set; } = "utf-8";

    /// <summary>Selected native-command argument-passing mode when exposed by the host.</summary>
    public string NativeArgumentPassing { get; set; } = string.Empty;
}

/// <summary>Final process-related state without claiming that a child-process launch was observed.</summary>
public sealed class PowerShellCompilationSemanticProcessStateObservation
{
    /// <summary>Final value of PowerShell's LASTEXITCODE variable, when it was set.</summary>
    public int? LastExitCode { get; set; }
}

/// <summary>One sequenced direct-child process effect observed by the semantic boundary.</summary>
public sealed class PowerShellCompilationSemanticProcessEffectObservation
{
    /// <summary>Order within the process-effect stream.</summary>
    public int Sequence { get; set; }

    /// <summary>Stable per-observation invocation ordinal shared by the launch and its optional exit.</summary>
    public int Invocation { get; set; }

    /// <summary>Effect kind: NativeProcessLaunch or NativeProcessExit.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Portable executable leaf name reported by the operating-system process boundary.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Observed native-process exit code for an exit effect; null for a launch.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Versioned direct-observation source. This must not describe syntax or LASTEXITCODE inference.</summary>
    public string ObservationSource { get; set; } = string.Empty;
}

/// <summary>Black-box interpreted-host execution request used by the semantic oracle runner.</summary>
public sealed class PowerShellCompilationSemanticOracleRequest
{
    /// <summary>Creates an oracle request for one script file and one immutable profile.</summary>
    public PowerShellCompilationSemanticOracleRequest(string profileId, string scriptPath)
    {
        ProfileId = string.IsNullOrWhiteSpace(profileId)
            ? throw new ArgumentException("A semantic profile identity is required.", nameof(profileId))
            : profileId.Trim();
        ScriptPath = string.IsNullOrWhiteSpace(scriptPath)
            ? throw new ArgumentException("A script path is required.", nameof(scriptPath))
            : System.IO.Path.GetFullPath(scriptPath.Trim().Trim('"'));
    }

    /// <summary>Semantic profile identity.</summary>
    public string ProfileId { get; }

    /// <summary>PowerShell script path.</summary>
    public string ScriptPath { get; }

    /// <summary>Literal string arguments passed positionally to the script.</summary>
    public string[] Arguments { get; set; } = Array.Empty<string>();

    /// <summary>Success-object properties explicitly permitted in the portable observation.</summary>
    public string[] ObservedPropertyNames { get; set; } = Array.Empty<string>();

    /// <summary>Culture used by the isolated host.</summary>
    public string Culture { get; set; } = "en-US";

    /// <summary>Isolated root whose file changes are recorded.</summary>
    public string? FileSystemRoot { get; set; }

    /// <summary>Execution-surface label.</summary>
    public string ExecutionSurface { get; set; } = "Interpreted";

    /// <summary>Optional exact executable path used instead of the profile's PATH-resolved host name.</summary>
    public string? HostExecutablePath { get; set; }

    /// <summary>Optional pre-recorded host-artifact identity required for this observation.</summary>
    public string? ExpectedHostArtifactSha256 { get; set; }

    /// <summary>Maximum host execution time.</summary>
    public int TimeoutSeconds { get; set; } = 120;
}

/// <summary>One semantic difference between two normalized oracle envelopes.</summary>
public sealed class PowerShellCompilationSemanticOracleDifference
{
    /// <summary>Creates a semantic difference.</summary>
    public PowerShellCompilationSemanticOracleDifference(string path, string expected, string actual)
    {
        Path = path ?? string.Empty;
        Expected = expected ?? string.Empty;
        Actual = actual ?? string.Empty;
    }

    /// <summary>Envelope path that differs.</summary>
    public string Path { get; }

    /// <summary>Expected normalized value.</summary>
    public string Expected { get; }

    /// <summary>Actual normalized value.</summary>
    public string Actual { get; }
}
