using System.Collections;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates configuration for file consistency checking (encoding and line endings) during module build.
/// </summary>
/// <remarks>
/// <para>
/// Adds a file-consistency validation step to the pipeline. This can enforce required encoding/line-ending rules
/// and (optionally) auto-fix issues during a build.
/// </para>
/// </remarks>
/// <example>
/// <summary>Enforce UTF-8 BOM + CRLF and auto-fix during build</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationFileConsistency -Enable -FailOnInconsistency -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF -AutoFix -CreateBackups -ExportReport</code>
/// <para>Enforces consistency and exports a CSV report; backups are created before fixes are applied.</para>
/// </example>
/// <example>
/// <summary>Audit project root consistency (no fixes)</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationFileConsistency -Enable -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF -ExportReport -Scope StagingAndProject</code>
/// <para>Runs validation on staging and project root, exports a report, and does not apply automatic fixes.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationFileConsistency")]
public sealed class NewConfigurationFileConsistencyCommand : PSCmdlet     
{
    /// <summary>Enable file consistency checking during build.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Fail the build if consistency issues are found.</summary>
    [Parameter] public SwitchParameter FailOnInconsistency { get; set; }

    /// <summary>Severity for consistency issues (overrides FailOnInconsistency when specified).</summary>
    [Parameter] public ValidationSeverity? Severity { get; set; }

    /// <summary>Required file encoding.</summary>
    [Parameter] public PowerForge.FileConsistencyEncoding RequiredEncoding { get; set; } = PowerForge.FileConsistencyEncoding.UTF8BOM;

    /// <summary>Required line ending style.</summary>
    [Parameter] public PowerForge.FileConsistencyLineEnding RequiredLineEnding { get; set; } = PowerForge.FileConsistencyLineEnding.CRLF;

    /// <summary>Project kind used to derive default include patterns.</summary>
    [Parameter] public ProjectKind? ProjectKind { get; set; }

    /// <summary>Custom include patterns (override default project kind patterns).</summary>
    [Parameter] public string[]? IncludePatterns { get; set; }

    /// <summary>Scope for file consistency checks (staging/project).</summary>
    [Parameter] public PowerForge.FileConsistencyScope Scope { get; set; } = PowerForge.FileConsistencyScope.StagingOnly;

    /// <summary>Automatically fix encoding and line ending issues during build.</summary>
    [Parameter] public SwitchParameter AutoFix { get; set; }

    /// <summary>Create backup files before applying automatic fixes.</summary>
    [Parameter] public SwitchParameter CreateBackups { get; set; }

    /// <summary>Maximum percentage of files that can have consistency issues. Default is 5.</summary>
    [Parameter] public int MaxInconsistencyPercentage { get; set; } = 5;

    /// <summary>Directory names to exclude from consistency analysis.</summary>
    [Parameter] public string[] ExcludeDirectories { get; set; } = new[] { "Artefacts", "Ignore", ".git", ".vs", "bin", "obj" };

    /// <summary>File patterns to exclude from consistency analysis.</summary>
    [Parameter] public string[]? ExcludeFiles { get; set; }

    /// <summary>Per-path encoding overrides (patterns mapped to encodings).</summary>
    [Parameter] public Hashtable? EncodingOverrides { get; set; }

    /// <summary>Per-path line ending overrides (patterns mapped to line endings).</summary>
    [Parameter] public Hashtable? LineEndingOverrides { get; set; }

    /// <summary>Export detailed consistency report to the artifacts directory.</summary>
    [Parameter] public SwitchParameter ExportReport { get; set; }

    /// <summary>Custom filename for the consistency report.</summary>
    [Parameter] public string ReportFileName { get; set; } = "FileConsistencyReport.csv";

    /// <summary>Check for files with mixed line endings.</summary>
    [Parameter] public SwitchParameter CheckMixedLineEndings { get; set; }

    /// <summary>Check for files missing final newlines.</summary>
    [Parameter] public SwitchParameter CheckMissingFinalNewline { get; set; }

    /// <summary>Legacy switch. When set, applies encoding/line-ending consistency fixes to the project root as well as staging output.</summary>
    [Parameter] public SwitchParameter UpdateProjectRoot { get; set; }

    /// <summary>Emits file-consistency configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var cfg = new FileConsistencyConfigurationFactory().Create(new FileConsistencyConfigurationRequest
        {
            Enable = Enable.IsPresent,
            FailOnInconsistency = FailOnInconsistency.IsPresent,
            Severity = Severity,
            RequiredEncoding = RequiredEncoding,
            RequiredLineEnding = RequiredLineEnding,
            ProjectKind = ProjectKind,
            ProjectKindSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(ProjectKind)),
            IncludePatterns = IncludePatterns,
            Scope = Scope,
            ScopeSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(Scope)),
            AutoFix = AutoFix.IsPresent,
            CreateBackups = CreateBackups.IsPresent,
            MaxInconsistencyPercentage = MaxInconsistencyPercentage,
            ExcludeDirectories = ExcludeDirectories,
            ExcludeFiles = ExcludeFiles,
            EncodingOverrides = EncodingOverrides,
            LineEndingOverrides = LineEndingOverrides,
            UpdateProjectRoot = UpdateProjectRoot.IsPresent,
            ExportReport = ExportReport.IsPresent,
            ReportFileName = ReportFileName,
            CheckMixedLineEndings = CheckMixedLineEndings.IsPresent,
            CheckMissingFinalNewline = CheckMissingFinalNewline.IsPresent
        });

        WriteObject(cfg);
    }
}
