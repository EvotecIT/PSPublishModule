using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Retrieves project version information from .csproj, .psd1, and build scripts.
/// </summary>
/// <remarks>
/// <para>
/// Scans the specified path for:
/// <list type="bullet">
/// <item><description><c>*.csproj</c> files</description></item>
/// <item><description><c>*.psd1</c> files</description></item>
/// <item><description>PowerShell build scripts (<c>*.ps1</c>) that contain <c>Invoke-ModuleBuild</c></description></item>
/// </list>
/// and returns one record per discovered version entry.
/// </para>
/// <para>
/// This is useful for multi-project repositories where you want to quickly verify version alignment across projects/modules.
/// </para>
/// </remarks>
/// <example>
/// <summary>Get version information for all projects in the current directory</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectVersion</code>
/// <para>Returns entries for discovered .csproj/.psd1/build scripts under the current folder.</para>
/// </example>
/// <example>
/// <summary>Filter results to one module name</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ProjectVersion -ModuleName 'MyModule' -Path 'C:\Projects'</code>
/// <para>Useful when a repository contains multiple modules/projects but you need only one.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ProjectVersion", SupportsShouldProcess = false)]
[OutputType(typeof(ProjectVersionInfo))]
public sealed class GetProjectVersionCommand : PSCmdlet
{
    /// <summary>Optional module name to filter .csproj and .psd1 results.</summary>
    [Parameter]
    public string? ModuleName { get; set; }

    /// <summary>The root path to search for project files. Defaults to current directory.</summary>
    [Parameter]
    public string Path { get; set; } = string.Empty;

    /// <summary>Path fragments (or folder names) to exclude from the search (in addition to default 'obj' and 'bin'). This matches against the full path, case-insensitively.</summary>
    [Parameter]
    public string[] ExcludeFolders { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Executes the search and emits one object per discovered project file version.
    /// </summary>
    protected override void ProcessRecord()
    {
        var root = string.IsNullOrWhiteSpace(Path) ? SessionState.Path.CurrentFileSystemLocation.Path : Path;
        var versions = new ProjectVersionService().Discover(new ProjectVersionQueryRequest
        {
            RootPath = root,
            ModuleName = ModuleName,
            ExcludeFolders = ExcludeFolders
        });
        foreach (var v in versions) WriteObject(v);
    }
}
