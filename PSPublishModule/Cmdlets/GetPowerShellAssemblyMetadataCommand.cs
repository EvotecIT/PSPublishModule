using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets the cmdlets and aliases in a .NET assembly by scanning for cmdlet-related attributes.
/// </summary>
/// <remarks>
/// <para>
/// This is typically used by module build tooling to determine which cmdlets and aliases should be exported
/// for binary modules (compiled cmdlets).
/// </para>
/// <para>
/// The cmdlet delegates scanning to a typed PowerForge service so the metadata logic can be reused outside
/// the PowerShell surface.
/// </para>
/// </remarks>
/// <example>
/// <summary>Inspect a compiled PowerShell module assembly</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellAssemblyMetadata -Path '.\bin\Release\net8.0\MyModule.dll'</code>
/// <para>Returns discovered cmdlet and alias names based on PowerShell attributes.</para>
/// </example>
/// <example>
/// <summary>Inspect an assembly in a build artifact folder</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Get-PowerShellAssemblyMetadata -Path 'C:\Artifacts\MyModule\Bin\MyModule.dll'</code>
/// <para>Useful when validating what will be exported before publishing.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "PowerShellAssemblyMetadata")]
public sealed class GetPowerShellAssemblyMetadataCommand : PSCmdlet
{
    /// <summary>The assembly to inspect.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Executes the assembly scan and returns cmdlets/aliases that should be exported.
    /// </summary>
    protected override void ProcessRecord()
    {
        var assemblyPath = System.IO.Path.GetFullPath(Path.Trim().Trim('"'));
        if (!File.Exists(assemblyPath))
        {
            WriteError(new ErrorRecord(
                new FileNotFoundException($"Assembly not found: {assemblyPath}"),
                "AssemblyNotFound",
                ErrorCategory.ObjectNotFound,
                assemblyPath));
            WriteObject(false);
            return;
        }

        WriteVerbose($"Loading assembly {assemblyPath}");

        try
        {
            var service = new PowerShellAssemblyMetadataService();
            WriteObject(service.Analyze(assemblyPath));
        }
        catch (Exception ex)
        {
            WriteWarning($"Can't load assembly {assemblyPath}. Error: {ex.Message}");
            WriteObject(false);
        }
    }
}
