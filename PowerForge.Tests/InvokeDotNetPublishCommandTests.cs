using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeDotNetPublishCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfigurationFailure_UsesTheSelectedPowerShellErrorContract(bool exitCode)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-publish-console-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "publish.json");
            File.WriteAllText(path, "{ invalid");
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-DotNetPublish", typeof(InvokeDotNetPublishCommand), null));
            using var shell = PowerShell.Create(state);
            shell.AddCommand("Invoke-DotNetPublish").AddParameter("ConfigPath", path).AddParameter("Quiet");
            if (exitCode) shell.AddParameter("ExitCode");
            var result = Assert.IsType<DotNetPublishResult>(Assert.Single(shell.Invoke()).BaseObject);
            Assert.False(result.Succeeded);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Equal(exitCode ? 0 : 1, shell.Streams.Error.Count);
        }
        finally { Directory.Delete(root, true); }
    }
}
