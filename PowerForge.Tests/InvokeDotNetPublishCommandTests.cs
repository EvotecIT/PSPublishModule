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

    [Theory]
    [InlineData(false, true, "Continue")]
    [InlineData(true, false, "Continue")]
    [InlineData(true, true, "SilentlyContinue")]
    [InlineData(false, true, "Stop")]
    [InlineData(true, false, "Stop")]
    public void OptionalHookWarning_PreservesPowerShellPreferences(bool quiet, bool exitCode, string warningAction)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-publish-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "App.cs"), "public class App { }");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            var windows = OperatingSystem.IsWindows();
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root, Build = false },
                Hooks = [new DotNetPublishCommandHook
                {
                    Id = "optional-warning", Phase = DotNetPublishCommandHookPhase.BeforeRestore, Required = false,
                    Command = windows ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
                    Arguments = windows ? ["/d", "/c", "exit 7"] : ["-c", "exit 7"]
                }],
                Targets = [new DotNetPublishTarget
                {
                    Name = "App", ProjectPath = "App.csproj", Kind = DotNetPublishTargetKind.Library,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0", Runtimes = [System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier],
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                }]
            };
            var path = Path.Combine(root, "publish.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(spec));
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-DotNetPublish", typeof(InvokeDotNetPublishCommand), null));
            using var shell = PowerShell.Create(state);
            shell.AddScript($"$ErrorActionPreference = 'Stop'; Invoke-DotNetPublish -ConfigPath '{path.Replace("'", "''")}' -NoInteractive {(quiet ? "-Quiet" : "")} {(exitCode ? "-ExitCode" : "")} -WarningAction {warningAction} -WarningVariable captured; 'continued'; $captured.Count; $captured[0].Message");
            System.Collections.ObjectModel.Collection<PSObject>? output = null;
            var error = Record.Exception(() => output = shell.Invoke());
            if (warningAction == "SilentlyContinue") Assert.Empty(shell.Streams.Warning);
            else Assert.Contains("optional-warning", Assert.Single(shell.Streams.Warning).Message);
            if (warningAction == "Stop")
            {
                Assert.True(error is not null || shell.HadErrors);
                Assert.DoesNotContain(output ?? [], item => item.BaseObject is DotNetPublishResult || Equals(item.BaseObject, "continued"));
                Assert.False(Directory.Exists(Path.Combine(root, "obj")));
            }
            else
            {
                Assert.Null(error);
                Assert.False(shell.HadErrors);
                Assert.True(Assert.IsType<DotNetPublishResult>(output![0].BaseObject).Succeeded);
                Assert.Equal("continued", output[1].BaseObject);
                Assert.Equal(1, output[2].BaseObject);
                Assert.Contains("optional-warning", Assert.IsType<string>(output[3].BaseObject));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
