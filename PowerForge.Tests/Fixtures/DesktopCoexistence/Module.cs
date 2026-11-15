using System.Management.Automation;
using DesktopCoexistence.Dependency;

namespace PowerForgeDesktopCoexistence
{
    // GetTypes must resolve the new dependency before the binary module can be imported.
    public sealed class DependencyDerived : CurrentOnly { }

    [Cmdlet(VerbsCommon.Get, "DesktopCoexistence")]
    [OutputType(typeof(string))]
    public sealed class GetDesktopCoexistenceCommand : PSCmdlet
    {
        protected override void ProcessRecord()
        {
            WriteObject(new DependencyDerived().Value);
        }
    }
}
