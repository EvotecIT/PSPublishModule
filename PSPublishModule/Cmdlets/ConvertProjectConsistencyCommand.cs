using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Converts a project to a consistent encoding/line ending policy and reports the results.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet applies a consistency policy (encoding and/or line endings) across a project tree.
/// It can also export a post-conversion report so you can validate what remains inconsistent.
/// </para>
/// <para>
/// For build-time enforcement, use <c>New-ConfigurationFileConsistency -AutoFix</c> in the module build pipeline.
/// </para>
/// </remarks>
/// <example>
/// <summary>Convert a PowerShell project to UTF-8 BOM + CRLF</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Convert-ProjectConsistency -Path 'C:\MyProject' -ProjectType PowerShell -CreateBackups</code>
/// <para>Ensures PowerShell-friendly encoding and line endings, creating backups before changes.</para>
/// </example>
/// <example>
/// <summary>Convert line endings only for a cross-platform repo</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Convert-ProjectConsistency -Path 'C:\MyProject' -FixLineEndings -RequiredLineEnding LF -ExcludeDirectories 'Build','Docs'</code>
/// <para>Normalizes line endings to LF and skips non-source folders.</para>
/// </example>
/// <example>
/// <summary>Convert encoding only with per-extension overrides</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Convert-ProjectConsistency -Path 'C:\MyProject' -FixEncoding -RequiredEncoding UTF8BOM -EncodingOverrides @{ '*.xml' = 'UTF8' } -ExportPath 'C:\Reports\consistency.csv'</code>
/// <para>Uses UTF-8 BOM by default but keeps XML files UTF-8 without BOM, and writes a report.</para>
/// </example>
[Cmdlet(VerbsData.Convert, "ProjectConsistency", SupportsShouldProcess = true)]
public sealed class ConvertProjectConsistencyCommand : PSCmdlet
{
    /// <summary>Path to the project directory to convert.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Type of project to analyze. Determines which file extensions are included.</summary>
    [Parameter]
    [ValidateSet("PowerShell", "CSharp", "Mixed", "All", "Custom")]
    public string ProjectType { get; set; } = "Mixed";

    /// <summary>Custom file extensions to include when ProjectType is Custom (e.g., *.ps1, *.cs).</summary>
    [Parameter]
    public string[]? CustomExtensions { get; set; }

    /// <summary>Directory names to exclude from conversion (e.g., .git, bin, obj).</summary>
    [Parameter]
    public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" };

    /// <summary>File patterns to exclude from conversion.</summary>
    [Parameter]
    public string[] ExcludeFiles { get; set; } = Array.Empty<string>();

    /// <summary>Target encoding to enforce when fixing encoding consistency.</summary>
    [Parameter]
    public FileConsistencyEncoding RequiredEncoding { get; set; } = FileConsistencyEncoding.UTF8BOM;

    /// <summary>Target line ending style to enforce when fixing line endings.</summary>
    [Parameter]
    public FileConsistencyLineEnding RequiredLineEnding { get; set; } = FileConsistencyLineEnding.CRLF;

    /// <summary>Source encoding filter. When Any, any non-target encoding may be converted.</summary>
    [Parameter]
    public TextEncodingKind SourceEncoding { get; set; } = TextEncodingKind.Any;

    /// <summary>Convert encoding inconsistencies.</summary>
    [Parameter]
    public SwitchParameter FixEncoding { get; set; }

    /// <summary>Convert line ending inconsistencies.</summary>
    [Parameter]
    public SwitchParameter FixLineEndings { get; set; }

    /// <summary>Per-path encoding overrides (hashtable of pattern => encoding).</summary>
    [Parameter]
    public IDictionary? EncodingOverrides { get; set; }

    /// <summary>Per-path line ending overrides (hashtable of pattern => line ending).</summary>
    [Parameter]
    public IDictionary? LineEndingOverrides { get; set; }

    /// <summary>Create backup files before modifying content.</summary>
    [Parameter]
    public SwitchParameter CreateBackups { get; set; }

    /// <summary>Backup root folder (mirrors the project structure).</summary>
    [Parameter]
    public string? BackupDirectory { get; set; }

    /// <summary>Force conversion even when the file already matches the target.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Do not rollback from backup if verification mismatch occurs during encoding conversion.</summary>
    [Parameter]
    public SwitchParameter NoRollbackOnMismatch { get; set; }

    /// <summary>Only convert files that have mixed line endings.</summary>
    [Parameter]
    public SwitchParameter OnlyMixedLineEndings { get; set; }

    /// <summary>Ensure a final newline exists after line ending conversion.</summary>
    [Parameter]
    public SwitchParameter EnsureFinalNewline { get; set; }

    /// <summary>Only fix files missing the final newline.</summary>
    [Parameter]
    public SwitchParameter OnlyMissingFinalNewline { get; set; }

    /// <summary>Include detailed file-by-file analysis in the output.</summary>
    [Parameter]
    public SwitchParameter ShowDetails { get; set; }

    /// <summary>Export the detailed report to a CSV file at the specified path.</summary>
    [Parameter]
    public string? ExportPath { get; set; }

    /// <summary>
    /// Executes the conversion and returns a combined result.
    /// </summary>
    protected override void ProcessRecord()
    {
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ProjectConsistencyWorkflowService(logger);
        var root = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        var request = new ProjectConsistencyWorkflowRequest
        {
            Path = Path,
            ProjectType = ProjectType,
            CustomExtensions = CustomExtensions,
            ExcludeDirectories = ExcludeDirectories,
            ExcludeFiles = ExcludeFiles,
            IncludeDetails = ShowDetails.IsPresent,
            ExportPath = ExportPath,
            RecommendedEncoding = RequiredEncoding.ToTextEncodingKind(),
            RecommendedLineEnding = RequiredLineEnding,
            EncodingOverrides = ProjectConsistencyWorkflowService.ParseEncodingOverrides(EncodingOverrides),
            LineEndingOverrides = ProjectConsistencyWorkflowService.ParseLineEndingOverrides(LineEndingOverrides),
            FixEncodingSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(FixEncoding)),
            FixEncoding = FixEncoding.IsPresent,
            FixLineEndingsSpecified = MyInvocation.BoundParameters.ContainsKey(nameof(FixLineEndings)),
            FixLineEndings = FixLineEndings.IsPresent,
            SourceEncoding = SourceEncoding,
            RequiredEncoding = RequiredEncoding,
            RequiredLineEnding = RequiredLineEnding,
            CreateBackups = CreateBackups.IsPresent,
            BackupDirectory = BackupDirectory,
            Force = Force.IsPresent,
            NoRollbackOnMismatch = NoRollbackOnMismatch.IsPresent,
            OnlyMixedLineEndings = OnlyMixedLineEndings.IsPresent,
            EnsureFinalNewline = EnsureFinalNewline.IsPresent,
            OnlyMissingFinalNewline = OnlyMissingFinalNewline.IsPresent
        };

        var shouldProcess = ShouldProcess(root, "Convert project consistency");
        if (shouldProcess)
        {
            var result = service.ConvertAndAnalyze(request);
            WriteSummary(result.RootPath, result.Report, result.EncodingConversion, result.LineEndingConversion);
            WriteObject(new ProjectConsistencyConversionResult(result.Report, result.EncodingConversion, result.LineEndingConversion));
            return;
        }

        WriteVerbose($"Project type: {ProjectType} with patterns: {string.Join(", ", ProjectConsistencyWorkflowService.ResolvePatterns(ProjectType, CustomExtensions))}");
        WriteWarning("WhatIf/ShouldProcess declined project consistency conversion.");
        WriteObject(new ProjectConsistencyConversionResult(
            service.Analyze(request).Report,
            null,
            null));
    }

    private void WriteSummary(string root, ProjectConsistencyReport report, ProjectConversionResult? encodingResult, ProjectConversionResult? lineEndingResult)
    {
        var s = report.Summary;
        HostWriteLineSafe("Project Consistency Conversion", ConsoleColor.Cyan);
        HostWriteLineSafe($"Project: {root}");
        HostWriteLineSafe($"Target encoding: {s.RecommendedEncoding}");
        HostWriteLineSafe($"Target line ending: {s.RecommendedLineEnding}");

        if (encodingResult is not null)
            HostWriteLineSafe(
                $"Encoding conversion: {encodingResult.Converted}/{encodingResult.Total} converted, {encodingResult.Skipped} skipped, {encodingResult.Errors} errors",
                encodingResult.Errors == 0 ? ConsoleColor.Green : ConsoleColor.Red);

        if (lineEndingResult is not null)
            HostWriteLineSafe(
                $"Line ending conversion: {lineEndingResult.Converted}/{lineEndingResult.Total} converted, {lineEndingResult.Skipped} skipped, {lineEndingResult.Errors} errors",
                lineEndingResult.Errors == 0 ? ConsoleColor.Green : ConsoleColor.Red);

        HostWriteLineSafe("");
        HostWriteLineSafe("Consistency summary:", ConsoleColor.Cyan);
        HostWriteLineSafe(
            $"  Files compliant: {s.FilesCompliant} ({s.CompliancePercentage.ToString("0.0", CultureInfo.InvariantCulture)}%)",
            s.CompliancePercentage >= 90 ? ConsoleColor.Green : s.CompliancePercentage >= 70 ? ConsoleColor.Yellow : ConsoleColor.Red);
        HostWriteLineSafe($"  Files needing attention: {s.FilesWithIssues}", s.FilesWithIssues == 0 ? ConsoleColor.Green : ConsoleColor.Red);

        if (!string.IsNullOrWhiteSpace(ExportPath) && File.Exists(ExportPath!))
        {
            HostWriteLineSafe("");
            HostWriteLineSafe($"Detailed report exported to: {ExportPath}", ConsoleColor.Green);
        }
    }

    private void HostWriteLineSafe(string text, ConsoleColor? fg = null)
    {
        try
        {
            if (fg.HasValue)
            {
                var bg = Host?.UI?.RawUI?.BackgroundColor ?? ConsoleColor.Black;
                Host?.UI?.WriteLine(fg.Value, bg, text);
            }
            else
            {
                Host?.UI?.WriteLine(text);
            }
        }
        catch
        {
            // ignore host limitations
        }
    }
}
