using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tests whether a script file contains readable PSResourceGet-compatible PSScriptInfo metadata.
/// </summary>
[Cmdlet(VerbsDiagnostic.Test, "ManagedScriptFileInfo")]
[OutputType(typeof(bool))]
public sealed class TestManagedScriptFileInfoCommand : PSCmdlet
{
    /// <summary>Path to the script file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var resolvedPath = ManagedScriptFileInfoCommandSupport.ResolvePath(this, Path);
        WriteObject(new ManagedScriptFileInfoService().Test(resolvedPath));
    }
}
