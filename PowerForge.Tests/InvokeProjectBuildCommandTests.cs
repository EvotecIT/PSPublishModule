using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokeProjectBuildCommandTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    public void ExitCode_ConvertsConfigurationExceptionsToFailedResult(string kind)
    {
        WithSigningFailureConfig(path =>
        {
            if (kind == "missing") File.Delete(path);
            else File.WriteAllText(path, "{ invalid");
            using var shell = CreateShell();
            shell.AddCommand("Invoke-ProjectBuild").AddParameter("ConfigPath", path)
                .AddParameter("Quiet").AddParameter("ExitCode");
            var result = Assert.IsType<ProjectBuildResult>(Assert.Single(shell.Invoke()).BaseObject);
            Assert.False(result.Success);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Empty(shell.Streams.Error);
        });
    }

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

    [Theory]
    [InlineData(false, true, "Continue")]
    [InlineData(true, false, "SilentlyContinue")]
    [InlineData(true, true, "Stop")]
    public void PlanWarning_UsesPowerShellWarningStream(bool quiet, bool exitCode, string preference)
    {
        WithSigningFailureConfig(path =>
        {
            File.WriteAllText(path, "{\"Build\":true,\"PackStrategy\":\"typo\"}");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(path)!, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version></PropertyGroup></Project>");
            using var shell = CreateShell();
            shell.AddScript($"$ErrorActionPreference='Stop'; Invoke-ProjectBuild -ConfigPath '{path.Replace("'", "''")}' -Plan -NoInteractive {(quiet ? "-Quiet" : "")} {(exitCode ? "-ExitCode" : "")} -WarningAction {preference} -WarningVariable captured; 'continued'; $captured[0].Message");
            System.Collections.ObjectModel.Collection<PSObject>? output = null;
            var error = Record.Exception(() => output = shell.Invoke());
            if (preference == "Stop")
            {
                Assert.True(error is not null || shell.HadErrors);
                Assert.DoesNotContain(output ?? [], item => item.BaseObject is ProjectBuildResult || Equals(item.BaseObject, "continued"));
            }
            else
            {
                Assert.Null(error);
                Assert.Equal("continued", output![1].BaseObject);
                Assert.Contains("Unknown PackStrategy", Assert.IsType<string>(output[2].BaseObject));
            }
            if (preference == "SilentlyContinue") Assert.Empty(shell.Streams.Warning);
            else Assert.Contains(shell.Streams.Warning, entry => entry.Message.Contains("Unknown PackStrategy"));
        });
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
