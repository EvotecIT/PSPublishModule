using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates version numbers across multiple project files.
/// </summary>
/// <remarks>
/// <para>
/// Updates version numbers in:
/// <list type="bullet">
/// <item><description>C# projects (<c>*.csproj</c>)</description></item>
/// <item><description>PowerShell module manifests (<c>*.psd1</c>)</description></item>
/// <item><description>PowerShell build scripts that reference <c>Invoke-ModuleBuild</c></description></item>
/// </list>
/// </para>
/// <para>
/// Use <c>-VersionType</c> to increment one component, or <c>-NewVersion</c> to set an explicit version.
/// </para>
/// </remarks>
/// <example>
/// <summary>Increment minor version across the repository</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Set-ProjectVersion -VersionType Minor -WhatIf</code>
/// <para>Previews the version update for all discovered project files.</para>
/// </example>
/// <example>
/// <summary>Set a specific version for one module</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Set-ProjectVersion -NewVersion '2.1.0' -ModuleName 'MyModule' -Path 'C:\Projects'</code>
/// <para>Updates only files related to the selected module name.</para>
/// </example>
[Cmdlet(VerbsCommon.Set, "ProjectVersion", SupportsShouldProcess = true)]
[OutputType(typeof(ProjectVersionUpdateResult))]
public sealed class SetProjectVersionCommand : PSCmdlet
{
    /// <summary>The type of version increment: Major, Minor, Build, or Revision.</summary>
    [Parameter]
    public ProjectVersionIncrementKind? VersionType { get; set; }

    /// <summary>Specific version number to set (format: x.x.x or x.x.x.x).</summary>
    [Parameter]
    [ValidatePattern("^\\d+\\.\\d+\\.\\d+(\\.\\d+)?$")]
    public string? NewVersion { get; set; }

    /// <summary>Optional module name to filter updates to specific projects/modules.</summary>
    [Parameter]
    public string? ModuleName { get; set; }

    /// <summary>The root path to search for project files. Defaults to current directory.</summary>
    [Parameter]
    public string Path { get; set; } = string.Empty;

    /// <summary>Path fragments (or folder names) to exclude from the search (in addition to default 'obj' and 'bin'). This matches against the full path, case-insensitively.</summary>
    [Parameter]
    public string[] ExcludeFolders { get; set; } = Array.Empty<string>();

    /// <summary>Returns per-file update results when specified.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>
    /// Executes the update across discovered project files.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = string.IsNullOrWhiteSpace(Path) ? SessionState.Path.CurrentFileSystemLocation.Path : Path;
        IReadOnlyList<ProjectVersionUpdateResult> results;
        try
        {
            results = new ProjectVersionService().Update(
                new ProjectVersionUpdateRequest
                {
                    RootPath = root,
                    ModuleName = ModuleName,
                    ExcludeFolders = ExcludeFolders,
                    NewVersion = NewVersion,
                    IncrementKind = VersionType
                },
                shouldProcess: (target, action) => ShouldProcess(target, action),
                verbose: WriteVerbose);
        }
        catch (InvalidOperationException ex)
        {
            WriteError(new ErrorRecord(
                ex,
                "NoProjectVersionFound",
                ErrorCategory.InvalidData,
                root));
            return;
        }
        catch (ArgumentException ex)
        {
            ThrowTerminatingError(new ErrorRecord(
                ex,
                "MissingVersionInput",
                ErrorCategory.InvalidArgument,
                null));
            return;
        }

        if (PassThru)
        {
            foreach (var r in results) WriteObject(r);
        }
    }
}
