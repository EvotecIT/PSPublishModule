using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeModuleBuildWarningTests
{
    [Theory]
    [InlineData(false, true, "Continue")]
    [InlineData(true, false, "SilentlyContinue")]
    [InlineData(true, true, "Stop")]
    public void LegacyWarning_PreservesCaptureAndStopBeforeJsonExport(bool quiet, bool exitCode, string preference)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-module-warning-" + Guid.NewGuid().ToString("N"));
        var module = Path.Combine(root, "WarningModule");
        Directory.CreateDirectory(module);
        try
        {
            File.WriteAllText(Path.Combine(module, "WarningModule.psm1"), "function Get-Probe { 'accepted' }");
            File.WriteAllText(Path.Combine(module, "WarningModule.psd1"), "@{RootModule='WarningModule.psm1';ModuleVersion='1.0.0';GUID='c228bb7b-2d97-4346-bc1e-16fe3281e30a';FunctionsToExport=@('Get-Probe')}");
            var json = Path.Combine(root, "export.json");
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-ModuleBuild", typeof(InvokeModuleBuildCommand), null));
            using var shell = PowerShell.Create(state);
            shell.AddScript($"$ErrorActionPreference='Stop'; Invoke-ModuleBuild -Path '{root.Replace("'", "''")}' -ModuleName WarningModule -Legacy -JsonOnly -JsonPath '{json.Replace("'", "''")}' -NoInteractive {(quiet ? "-Quiet" : "")} {(exitCode ? "-ExitCode" : "")} -WarningAction {preference} -WarningVariable captured; 'continued'; $captured[0].Message");
            System.Collections.ObjectModel.Collection<PSObject>? output = null;
            var error = Record.Exception(() => output = shell.Invoke());
            if (preference == "Stop")
            {
                Assert.True(error is not null || shell.HadErrors);
                Assert.DoesNotContain(output ?? [], item => Equals(item.BaseObject, "continued"));
                Assert.False(File.Exists(json));
            }
            else
            {
                Assert.Null(error);
                Assert.Equal("continued", output![0].BaseObject);
                Assert.Contains("Legacy PowerShell build pipeline", Assert.IsType<string>(output[1].BaseObject));
                Assert.True(File.Exists(json));
            }
            if (preference == "SilentlyContinue") Assert.Empty(shell.Streams.Warning);
            else Assert.Contains(shell.Streams.Warning, entry => entry.Message.Contains("Legacy PowerShell build pipeline"));
        }
        finally { Directory.Delete(root, true); }
    }
}
