using System.Management.Automation;
using System.Management.Automation.Runspaces;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class InvokePowerForgeReleaseWarningTests
{
    [Theory]
    [InlineData(false, "Continue")]
    [InlineData(false, "SilentlyContinue")]
    [InlineData(false, "Stop")]
    [InlineData(true, "Stop")]
    public void ChildLaneWarning_PreservesPowerShellPreferences(bool toolLane, string preference)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-unified-warning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>1.0.0</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "App.cs"), "public class App { }");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            var windows = OperatingSystem.IsWindows();
            var spec = new PowerForgeReleaseSpec
            {
                Packages = toolLane ? null : new ProjectBuildConfiguration { RootPath = ".", Build = true, PackStrategy = "typo" },
                Tools = toolLane ? new PowerForgeToolReleaseSpec
                {
                    DotNetPublish = new DotNetPublishSpec
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
                    }
                } : null
            };
            var path = Path.Combine(root, "release.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(spec));
            var state = InitialSessionState.CreateDefault();
            state.Commands.Add(new SessionStateCmdletEntry("Invoke-PowerForgeRelease", typeof(InvokePowerForgeReleaseCommand), null));
            using var shell = PowerShell.Create(state);
            shell.AddScript($"$ErrorActionPreference='Stop'; Invoke-PowerForgeRelease -ConfigPath '{path.Replace("'", "''")}' -NoInteractive -ExitCode {(toolLane ? "-ToolsOnly -SkipReleaseChecksums" : "-PackagesOnly -Plan")} -WarningAction {preference} -WarningVariable captured; 'continued'; $captured[0].Message");
            System.Collections.ObjectModel.Collection<PSObject>? output = null;
            var error = Record.Exception(() => output = shell.Invoke());
            var expected = toolLane ? "optional-warning" : "Unknown PackStrategy";
            if (preference == "Stop")
            {
                Assert.True(error is not null || shell.HadErrors);
                Assert.DoesNotContain(output ?? [], item => item.BaseObject is PowerForgeReleaseResult || Equals(item.BaseObject, "continued"));
                Assert.False(Directory.Exists(Path.Combine(root, "obj")));
            }
            else
            {
                Assert.Null(error);
                Assert.True(Assert.IsType<PowerForgeReleaseResult>(output![0].BaseObject).Success);
                Assert.Equal("continued", output[1].BaseObject);
                Assert.Contains(expected, Assert.IsType<string>(output[2].BaseObject));
            }
            if (preference == "SilentlyContinue") Assert.Empty(shell.Streams.Warning);
            else Assert.Contains(shell.Streams.Warning, entry => entry.Message.Contains(expected));
        }
        finally { Directory.Delete(root, true); }
    }
}
