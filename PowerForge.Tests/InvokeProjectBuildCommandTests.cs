using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeProjectBuildCommandTests
{
    [Theory]
    [InlineData("Quiet")]
    [InlineData("NoInteractive")]
    public void FailedBuild_HonorsErrorActionStop(string outputMode)
    {
        WithSigningFailureConfig(path =>
        {
            using var shell = CreateShell();
            shell.AddCommand("Invoke-ProjectBuild").AddParameter("ConfigPath", path)
                .AddParameter(outputMode).AddParameter("ErrorAction", ActionPreference.Stop);
            var error = Assert.ThrowsAny<RuntimeException>(() => shell.Invoke());
            Assert.Contains("CertificateThumbprint", error.Message);
        });
    }

    [Fact]
    public void FailedQuietBuild_EmitsErrorAndFailedResultWithContinuePreference()
    {
        WithSigningFailureConfig(path =>
        {
            using var shell = CreateShell();
            shell.AddCommand("Invoke-ProjectBuild").AddParameter("ConfigPath", path)
                .AddParameter("Quiet").AddParameter("ErrorAction", ActionPreference.Continue);
            var result = Assert.IsType<ProjectBuildResult>(Assert.Single(shell.Invoke()).BaseObject);
            Assert.False(result.Success);
            Assert.Contains(shell.Streams.Error, error => error.FullyQualifiedErrorId.StartsWith("InvokeProjectBuildFailed"));
        });
    }

    private static PowerShell CreateShell()
    {
        var state = InitialSessionState.CreateDefault();
        state.Commands.Add(new SessionStateCmdletEntry("Invoke-ProjectBuild", typeof(InvokeProjectBuildCommand), null));
        return PowerShell.Create(state);
    }

    private static void WithSigningFailureConfig(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-project-command-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "project.build.json");
            File.WriteAllText(path, "{\"Build\":true,\"SignAssemblies\":true,\"SignPackages\":false}");
            action(path);
        }
        finally { Directory.Delete(root, true); }
    }
}
