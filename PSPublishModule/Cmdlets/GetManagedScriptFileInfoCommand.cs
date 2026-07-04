using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Reads PSResourceGet-compatible PSScriptInfo metadata from a local script file.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ManagedScriptFileInfo")]
[OutputType(typeof(ManagedScriptFileInfo))]
public sealed class GetManagedScriptFileInfoCommand : PSCmdlet
{
    /// <summary>Path to the script file.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        try
        {
            var resolvedPath = ManagedScriptFileInfoCommandSupport.ResolvePath(this, Path);
            WriteObject(new ManagedScriptFileInfoService().Read(resolvedPath));
        }
        catch (Exception ex)
        {
            WriteError(new ErrorRecord(ex, "GetManagedScriptFileInfoFailed", ErrorCategory.NotSpecified, Path));
            throw;
        }
    }
}
