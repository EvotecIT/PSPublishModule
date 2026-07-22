using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class PowerForgeToolReleaseServiceTests
{
    [Fact]
    public void Plan_AppliesSharedReleaseVersionToLegacyToolOutputsAndMsBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeToolReleaseService(new NullLogger());
            var plan = service.Plan(
                new PowerForgeToolReleaseSpec
                {
                    ProjectRoot = root,
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTarget
                        {
                            Name = "Sample.Tool",
                            ProjectPath = "Sample.Tool.csproj",
                            Frameworks = new[] { "net10.0" },
                            Runtimes = new[] { "win-x64" },
                            OutputPath = "artifacts/{version}/{rid}"
                        }
                    }
                },
                Path.Combine(root, "release.json"),
                new PowerForgeReleaseRequest { ResolvedReleaseVersion = "3.1.0-preview.2" });

            var target = Assert.Single(plan.Targets);
            Assert.Equal("3.1.0-preview.2", target.Version);
            Assert.Equal("3.1.0-preview.2", target.MsBuildProperties["Version"]);
            Assert.Equal("3.1.0", target.MsBuildProperties["VersionPrefix"]);
            Assert.Contains(Path.Combine("artifacts", "3.1.0-preview.2", "win-x64"), Assert.Single(target.Combinations).OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UsesAnIsolatedNuGetLockFileForRuntimePublishes()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            ProcessStartInfo? captured = null;
            var service = new PowerForgeToolReleaseService(
                new NullLogger(),
                startInfo =>
                {
                    captured = startInfo;
                    var publishDirectory = ReadProperty(startInfo.Arguments, "/p:PublishDir=");
                    Directory.CreateDirectory(publishDirectory);
                    File.WriteAllText(Path.Combine(publishDirectory, "Sample.Tool"), "tool");
                    return new PowerForgeToolReleaseService.ProcessExecutionResult(0, string.Empty, string.Empty);
                });
            var outputPath = Path.Combine(root, "output");
            var result = service.Run(new PowerForgeToolReleasePlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets = new[]
                {
                    new PowerForgeToolReleaseTargetPlan
                    {
                        Name = "Sample.Tool",
                        ProjectPath = projectPath,
                        OutputName = "Sample.Tool",
                        Version = "1.2.3",
                        ArtifactRootPath = outputPath,
                        KeepDocs = true,
                        KeepSymbols = true,
                        MsBuildProperties = new Dictionary<string, string>(),
                        Combinations = new[]
                        {
                            new PowerForgeToolReleaseCombinationPlan
                            {
                                Runtime = "osx-arm64",
                                Framework = "net10.0",
                                Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                OutputPath = outputPath
                            }
                        }
                    }
                }
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Contains("/p:RestoreLockedMode=false", captured!.Arguments, StringComparison.Ordinal);
            Assert.Contains(
                "/p:NuGetLockFilePath=obj/PowerForge.ToolRelease.packages.lock.json",
                captured.Arguments,
                StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string ReadProperty(string arguments, string prefix)
    {
        var start = arguments.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Property '{prefix}' was not present in '{arguments}'.");
        start += prefix.Length;
        if (arguments[start] == '"')
        {
            var endQuote = arguments.IndexOf('"', start + 1);
            Assert.True(endQuote > start);
            return arguments.Substring(start + 1, endQuote - start - 1);
        }

        var end = arguments.IndexOf(' ', start);
        return end < 0 ? arguments.Substring(start) : arguments.Substring(start, end - start);
    }
}
