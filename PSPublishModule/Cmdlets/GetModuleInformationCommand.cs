using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets module manifest information from a project directory.
/// </summary>
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

