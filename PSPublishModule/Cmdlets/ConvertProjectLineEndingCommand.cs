using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Converts line endings across a project (CRLF/LF), with options for mixed-only fixes and final newline enforcement.
/// Thin wrapper over PowerForge.LineEndingConverter.
/// </summary>
/// <remarks>
/// <para>
/// Use <c>-WhatIf</c> to preview changes without modifying files. When conversion is performed, PowerForge preserves file encoding where possible
/// and prefers UTF-8 BOM for PowerShell files to maintain Windows PowerShell 5.1 compatibility.
/// </para>
/// </remarks>
/// <example>
///   <summary>Preview CRLF normalization for PowerShell files</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectLineEnding -Path C:\Repo -ProjectType PowerShell -TargetLineEnding CRLF -WhatIf</code>
///   <para>Shows which files would be normalized to Windows-style line endings.</para>
/// </example>
/// <example>
///   <summary>Fix only mixed line endings</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectLineEnding -Path . -ProjectType Mixed -TargetLineEnding LF -OnlyMixed -CreateBackups -BackupDirectory C:\Backups\Repo</code>
///   <para>Converts only files that contain both CRLF and LF, backing up originals first.</para>
/// </example>
/// <example>
///   <summary>Ensure final newline only</summary>
///   <prefix>PS&gt; </prefix>
///   <code>Convert-ProjectLineEnding -Path . -ProjectType All -TargetLineEnding CRLF -OnlyMissingNewline -EnsureFinalNewline</code>
///   <para>Appends a final CRLF to files missing it without changing other files.</para>
/// </example>
[Cmdlet(VerbsData.Convert, "ProjectLineEnding", SupportsShouldProcess = true, DefaultParameterSetName = "ProjectType")]
public sealed class ConvertProjectLineEndingCommand : PSCmdlet
{
    /// <summary>Path to the project directory to process.</summary>
    [Parameter(Mandatory = true)] public string Path { get; set; } = string.Empty;
    /// <summary>Project type used to derive default include patterns.</summary>
    [Parameter] public ProjectKind ProjectType { get; set; } = ProjectKind.Mixed;
    /// <summary>Custom extension patterns (e.g., *.ps1,*.psm1) when overriding defaults.</summary>
    [Parameter(ParameterSetName = "Custom")] public string[]? CustomExtensions { get; set; }
    /// <summary>Target line ending style to enforce.</summary>
    [Parameter(Mandatory = true)] public LineEnding TargetLineEnding { get; set; } = LineEnding.CRLF;
    /// <summary>Directory names to exclude from traversal.</summary>
    [Parameter] public string[] ExcludeDirectories { get; set; } = new[] { ".git", ".vs", "bin", "obj", "packages", "node_modules", ".vscode" };
    /// <summary>Create backups prior to conversion.</summary>
    [Parameter] public SwitchParameter CreateBackups { get; set; }
    /// <summary>Root folder for mirrored backups; when null, .bak is used next to files.</summary>
    [Parameter] public string? BackupDirectory { get; set; }
    /// <summary>Force conversion even if file appears to already match the target style.</summary>
    [Parameter] public SwitchParameter Force { get; set; }
    /// <summary>Convert only files that contain mixed line endings.</summary>
    [Parameter] public SwitchParameter OnlyMixed { get; set; }
    /// <summary>Ensure a final newline exists at the end of file.</summary>
    [Parameter] public SwitchParameter EnsureFinalNewline { get; set; }
    /// <summary>Only modify files that are missing the final newline.</summary>
    [Parameter] public SwitchParameter OnlyMissingNewline { get; set; }
    /// <summary>Emit per-file results instead of a summary.</summary>
    [Parameter] public SwitchParameter PassThru { get; set; }

    /// <summary>Executes the conversion using PowerForge.LineEndingConverter.</summary>
    protected override void ProcessRecord()
    {
        var enumeration = new ProjectEnumeration(
            rootPath: System.IO.Path.GetFullPath(Path.Trim().Trim('"')),
            kind: ProjectType,
            customExtensions: ParameterSetName == "Custom" ? CustomExtensions : null,
            excludeDirectories: ExcludeDirectories
        );

        var opts = new LineEndingConversionOptions(
            enumeration: enumeration,
            target: TargetLineEnding,
            createBackups: CreateBackups.IsPresent,
            backupDirectory: BackupDirectory,
            force: Force.IsPresent,
            onlyMixed: OnlyMixed.IsPresent,
            ensureFinalNewline: EnsureFinalNewline.IsPresent,
            onlyMissingNewline: OnlyMissingNewline.IsPresent,
            preferUtf8BomForPowerShell: true
        );

        var svc = new LineEndingConverter();
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
            Host.UI.WriteLine($"Line Endings Summary ({enumeration.RootPath})");
            Host.UI.WriteLine($"  Total files processed: {res.Total}");
            Host.UI.WriteLine($"  Successfully converted: {res.Converted}");
            Host.UI.WriteLine($"  Skipped: {res.Skipped}");
            Host.UI.WriteLine($"  Errors: {res.Errors}");
            Host.UI.WriteLine($"  Target: {TargetLineEnding}");
        }
    }
}
