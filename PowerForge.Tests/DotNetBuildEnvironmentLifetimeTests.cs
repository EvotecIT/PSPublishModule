using System.Diagnostics;
using System.Reflection;

namespace PowerForge.Tests;

public sealed class DotNetBuildEnvironmentLifetimeTests
{
    private static readonly string[] InheritedNames =
    [
        DotNetProcessLifetime.DisableNodeReuseEnvironmentVariable,
        DotNetProcessLifetime.DisableCliMsBuildServerEnvironmentVariable,
        "UNAPPROVED_AMBIENT_BUILD_VALUE"
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StrictEnvironment_PreservesLifetimeDefaultsInActualMsBuild(bool configureRuntime)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-strict-build-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Path.Combine(root, "Environment.proj");
            File.WriteAllText(project, """
                <Project>
                  <Target Name="CaptureEnvironment">
                    <PropertyGroup>
                      <NodeReuse>$([System.Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE'))</NodeReuse>
                      <BuildServer>$([System.Environment]::GetEnvironmentVariable('DOTNET_CLI_USE_MSBUILD_SERVER'))</BuildServer>
                    </PropertyGroup>
                    <WriteLinesToFile File="environment.txt" Lines="$(NodeReuse)|$(BuildServer)" Overwrite="true" />
                    <Error Condition="'$(NodeReuse)' != '1' Or '$(BuildServer)' != '0'" Text="Strict filtering removed captured-build lifetime defaults: $(NodeReuse)|$(BuildServer)" />
                  </Target>
                </Project>
                """);
            var environment = CreateEnvironment(configureRuntime, null);
            Assert.Null(environment["UNAPPROVED_AMBIENT_BUILD_VALUE"]);
            var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "dotnet", root, ["msbuild", project, "-t:CaptureEnvironment", "--nologo"], TimeSpan.FromSeconds(30), environment));

            Assert.True(result.Succeeded, result.StdOut + result.StdErr);
            Assert.Equal("1|0", File.ReadAllText(Path.Combine(root, "environment.txt")).Trim());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(DotNetProcessLifetime.DisableNodeReuseEnvironmentVariable, "0")]
    [InlineData(DotNetProcessLifetime.DisableNodeReuseEnvironmentVariable, null)]
    [InlineData(DotNetProcessLifetime.DisableCliMsBuildServerEnvironmentVariable, "1")]
    [InlineData(DotNetProcessLifetime.DisableCliMsBuildServerEnvironmentVariable, null)]
    public void StrictEnvironment_PreservesExplicitLifetimeOverrides(string name, string? value)
    {
        var environment = CreateEnvironment(true, new Dictionary<string, string?> { [name] = value });
        var request = new ProcessRunRequest("dotnet", Path.GetTempPath(), ["--version"], TimeSpan.FromSeconds(30), environment);
        var method = typeof(ProcessRunner).GetMethod("BuildStartInfo", BindingFlags.Static | BindingFlags.NonPublic)!;
        var startInfo = Assert.IsType<ProcessStartInfo>(method.Invoke(null, [request]));

        if (value is null) Assert.False(startInfo.EnvironmentVariables.ContainsKey(name));
        else Assert.Equal(value, startInfo.EnvironmentVariables[name]);
    }

    private static IReadOnlyDictionary<string, string?> CreateEnvironment(bool configureRuntime, IReadOnlyDictionary<string, string?>? overrides)
        => configureRuntime
            ? DotNetPublishPipelineRunner.CreateSafeDotNetChildEnvironment(overrides, InheritedNames, removeUnapprovedAmbient: true)
            : DotNetPublishPipelineRunner.CreateSafeMsBuildChildEnvironment(overrides, InheritedNames, removeUnapprovedAmbient: true);
}
