using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Converts file encodings across a project. Thin wrapper over PowerForge.EncodingConverter.
/// Defaults to UTF-8 with BOM for PowerShell file types to ensure PS 5.1 compatibility.
/// </summary>
/// <example>
///   <summary>Preview conversion for a PowerShell project</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectEncoding -Path C:\MyProject -ProjectType PowerShell -WhatIf</code>
///   <para>Shows what would be converted. PowerShell files default to UTF-8 with BOM.</para>
/// </example>
/// <example>
///   <summary>Convert ASCII files to UTF-8 with BOM and create backups</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectEncoding -Path C:\Repo -ProjectType Mixed -SourceEncoding ASCII -TargetEncoding UTF8BOM -CreateBackups -BackupDirectory C:\Backups\Repo</code>
///   <para>Only files detected as ASCII are converted; backups are mirrored under C:\Backups\Repo.</para>
/// </example>
/// <example>
///   <summary>Use custom patterns and return per-file results</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectEncoding -Path . -CustomExtensions *.ps1,*.psm1 -TargetEncoding UTF8BOM -PassThru</code>
///   <para>Processes only PowerShell scripts using custom patterns and outputs a detailed result object.</para>
/// </example>
[Cmdlet(VerbsData.Convert, "ProjectEncoding", SupportsShouldProcess = true, DefaultParameterSetName = "ProjectType")]
public sealed class ConvertProjectEncodingCommand : PSCmdlet
{
    /// <summary>Path to the project directory to process.</summary>
    [Parameter(Mandatory = true)] public string Path { get; set; } = string.Empty;
    /// <summary>Project type used to derive default include patterns.</summary>
    [Parameter(ParameterSetName = "ProjectType")] public ProjectKind ProjectType { get; set; } = ProjectKind.Mixed;
    /// <summary>Custom extension patterns (e.g., *.ps1,*.psm1) when overriding defaults.</summary>
    [Parameter(ParameterSetName = "Custom", Mandatory = true)] public string[]? CustomExtensions { get; set; }
    /// <summary>Expected source encoding; when Any, any non-target encoding may be converted.</summary>
    [Parameter] public TextEncodingKind SourceEncoding { get; set; } = TextEncodingKind.Any;
    /// <summary>Explicit target encoding; when null, defaults are chosen based on file type.</summary>
    [Parameter] public TextEncodingKind? TargetEncoding { get; set; }
    /// <summary>Directory names to exclude from traversal.</summary>
    [Parameter] public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" };
    /// <summary>Create backups prior to conversion.</summary>
    [Parameter] public SwitchParameter CreateBackups { get; set; }
    /// <summary>Root folder for mirrored backups; when null, .bak is used next to files.</summary>
    [Parameter] public string? BackupDirectory { get; set; }
    /// <summary>Force conversion even if detection does not match SourceEncoding.</summary>
    [Parameter] public SwitchParameter Force { get; set; }
    /// <summary>Do not rollback from backup if verification mismatch occurs.</summary>
    [Parameter] public SwitchParameter NoRollbackOnMismatch { get; set; }
    /// <summary>Emit per-file results instead of a summary.</summary>
    [Parameter] public SwitchParameter PassThru { get; set; }

    /// <summary>Executes the conversion using PowerForge.EncodingConverter.</summary>
    protected override void ProcessRecord()
    {
        var enumeration = new ProjectEnumeration(
            rootPath: System.IO.Path.GetFullPath(Path.Trim().Trim('"')),
            kind: ProjectType,
            customExtensions: ParameterSetName == "Custom" ? CustomExtensions : null,
            excludeDirectories: ExcludeDirectories
        );

        var opts = new EncodingConversionOptions(
            enumeration: enumeration,
            sourceEncoding: SourceEncoding,
            targetEncoding: TargetEncoding,
            createBackups: CreateBackups.IsPresent,
            backupDirectory: BackupDirectory,
            force: Force.IsPresent,
            noRollbackOnMismatch: NoRollbackOnMismatch.IsPresent,
            preferUtf8BomForPowerShell: true
        );

        var svc = new EncodingConverter();
        var res = svc.Convert(opts);

        if (PassThru)
        {
            foreach (var f in res.Files)
            {
                WriteObject(new PSObject(new { f.Path, f.Source, f.Target, f.Status, f.BackupPath, f.Error }));
            }
        }
        else
        {
            Host.UI.WriteLine("");
            Host.UI.WriteLine($"Conversion Summary ({enumeration.RootPath})");
            Host.UI.WriteLine($"  Total files processed: {res.Total}");
            Host.UI.WriteLine($"  Successfully converted: {res.Converted}");
            Host.UI.WriteLine($"  Skipped: {res.Skipped}");
            Host.UI.WriteLine($"  Errors: {res.Errors}");
            var targetLabel = TargetEncoding?.ToString() ?? (ProjectType == ProjectKind.PowerShell ? nameof(TextEncodingKind.UTF8BOM) : nameof(TextEncodingKind.UTF8));
            Host.UI.WriteLine($"  Encoding: {SourceEncoding} -> {targetLabel}");
        }
    }
}
