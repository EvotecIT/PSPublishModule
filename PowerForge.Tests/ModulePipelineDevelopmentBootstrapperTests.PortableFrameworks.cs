using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineDevelopmentBootstrapperTests
{
    [Fact]
    public void Run_Net8OnlyBuild_PreservesConditionalDesktopFrameworkInSourceLoader()
    {
        var tempRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "DemoModule";
            var projectRoot = Directory.CreateDirectory(Path.Combine(tempRoot.FullName, "repo"));
            ModulePipelineMissingAnalysisServiceTests.WriteMinimalModule(projectRoot.FullName, moduleName, "1.0.0");
            var csprojDir = Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "Sources", "Demo"));
            File.WriteAllText(Path.Combine(csprojDir.FullName, "Demo.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0</TargetFrameworks>
                    <TargetFrameworks Condition="$([MSBuild]::IsOSPlatform('Windows'))">net472;net8.0</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(csprojDir.FullName, "Demo.cs"), "public sealed class Demo { }");

            var spec = CreateDevelopmentBinarySpec(
                projectRoot.FullName,
                moduleName,
                ModuleDevelopmentBinaryMode.Auto,
                ModuleDevelopmentSourceBootstrapperMode.ReplaceSingleFile);
            spec.Build.Frameworks = new[] { "net8.0" };
            spec.Build.StagingPath = Path.Combine(tempRoot.FullName, "staging");
            spec.Build.DisableBinaryCmdletScan = true;

            var result = new ModulePipelineRunner(new NullLogger()).Run(spec);

            Assert.Equal(new[] { "net8.0" }, result.Plan.BuildSpec.Frameworks);
            var libPath = Path.Combine(result.BuildResult.StagingPath, "Lib");
            Assert.Equal(new[] { "Core" }, Directory.GetDirectories(libPath).Select(Path.GetFileName));
            Assert.True(File.Exists(Path.Combine(libPath, "Core", "Demo.dll")));
            Assert.Equal("net8.0", File.ReadAllText(Path.Combine(libPath, "Core", "PowerForge.TargetFramework.txt")));

            var sourceLoader = File.ReadAllText(Path.Combine(projectRoot.FullName, moduleName + ".psm1"));
            Assert.Contains("$PowerForgeDevelopmentCoreFrameworks = @('net8.0', 'net472')", sourceLoader);
            Assert.Contains("$PowerForgeDevelopmentDesktopFrameworks = @('net472', 'net8.0')", sourceLoader);
        }
        finally
        {
            try { tempRoot.Delete(recursive: true); } catch { /* best effort */ }
        }
    }
}
