using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets module manifest information from a project directory.
/// </summary>
/// <remarks>
/// <para>
/// This is a lightweight helper used by build/publish commands.
/// It finds the module manifest (<c>*.psd1</c>) under <c>-Path</c> and returns a structured object
/// containing common fields such as module name, version, required modules, and the manifest path.
/// </para>
/// <para>
/// Use it in build scripts to avoid re-implementing manifest discovery logic.
/// </para>
/// </remarks>
/// <example>
/// <summary>Read module information from a module folder</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-ModuleInformation -Path 'C:\Git\MyModule\Module'</code>
/// <para>Returns the parsed manifest and convenience properties such as module name and version.</para>
/// </example>
/// <example>
/// <summary>Use in a build script (relative to the script root)</summary>
/// <prefix>PS&gt; </prefix>
/// <code>$moduleInfo = Get-ModuleInformation -Path $PSScriptRoot; $moduleInfo.ManifestPath</code>
/// <para>Loads the manifest from the folder where the build script resides.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ModuleInformation")]
public sealed class GetModuleInformationCommand : PSCmdlet
{
    /// <summary>The path to the directory containing the module manifest file.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Executes the manifest discovery and returns a typed module-information object.
    /// </summary>
    protected override void ProcessRecord()
    {
        var reader = new ModuleInformationReader();
        var info = reader.Read(Path);
        WriteObject(info);
    }
}
