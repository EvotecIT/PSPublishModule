using PowerForge;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_EmitSourcePublishesHashedRebuildableProject()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-GeneratedProof { param([int] $Value) return $Value }");
        var spec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(Path.Combine(fixture.OutputPath, "GeneratedProof.generated"), result.GeneratedSourcePath);
        Assert.Equal(result.GeneratedSourcePath, result.Manifest!.GeneratedSourcePath);
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs")));
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "GeneratedProof.csproj")));
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "Directory.Build.props")));
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "Directory.Build.targets")));
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "Directory.Packages.props")));
        Assert.True(File.Exists(Path.Combine(result.GeneratedSourcePath!, "global.json")));
        var sourceMapPath = Path.Combine(result.GeneratedSourcePath!, "source-map.json");
        Assert.True(File.Exists(sourceMapPath));
        using (var sourceMap = JsonDocument.Parse(File.ReadAllText(sourceMapPath)))
        {
            Assert.Equal("input.ps1", sourceMap.RootElement.GetProperty("rootSource").GetString());
            var method = Assert.Single(sourceMap.RootElement.GetProperty("methods").EnumerateArray());
            Assert.Equal("Get-GeneratedProof", method.GetProperty("powershellName").GetString());
            Assert.Equal("input.ps1", method.GetProperty("sourceFile").GetString());
            Assert.Equal(1, method.GetProperty("sourceLine").GetInt32());
        }
        Assert.Contains(result.Manifest.Files, file => file.Role == "GeneratedSource" && file.Path.EndsWith("CompiledPowerShell.cs", StringComparison.Ordinal));
        Assert.Contains(result.Manifest.Files, file => file.Role == "GeneratedProject" && file.Path.EndsWith("GeneratedProof.csproj", StringComparison.Ordinal));
        Assert.Contains(result.Manifest.Files, file => file.Role == "GeneratedSourceMap" && file.Path.EndsWith("source-map.json", StringComparison.Ordinal));
        Assert.Equal(4, result.Manifest.Files.Count(file => file.Role == "GeneratedBuildIsolation"));
        Assert.All(
            result.Manifest.Files.Where(file => file.Path.StartsWith(result.GeneratedSourcePath!, StringComparison.OrdinalIgnoreCase)),
            file =>
            {
                Assert.NotEmpty(file.Sha256);
                Assert.Equal(new FileInfo(file.Path).Length, file.SizeBytes);
            });
    }

    [Fact]
    public void Build_EmittedProjectIsShieldedFromAncestorMsBuildAndSdkPolicy()
    {
        using var fixture = ArtifactFixture.Create("function Get-IsolatedProof { return 42 }");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Directory.Build.targets"), "<Project><Target Name=\"RejectInheritedBuild\" BeforeTargets=\"Build\"><Error Text=\"ancestor target imported\" /></Target></Project>");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Directory.Packages.props"), "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture.RootPath, "global.json"), "{ \"sdk\": { \"version\": \"3.1.100\", \"rollForward\": \"disable\" } }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedIsolation",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = result.GeneratedSourcePath!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add("GeneratedIsolation.csproj");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "Independent generated-project rebuild did not exit within 120 seconds.");
        Assert.True(process.ExitCode == 0, error + Environment.NewLine + output);
        Assert.DoesNotContain("ancestor target imported", output + error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("net8.0", "8.0.4")]
    [InlineData("net10.0", "10.0.11")]
    public void Build_EmittedBinaryModulePinsServicedSecurityXmlDependency(string targetFramework, string expectedVersion)
    {
        using var fixture = ArtifactFixture.Create("function Get-SecurityDependencyProof { return 42 }");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedSecurityDependency",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true,
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var project = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "GeneratedSecurityDependency.csproj"));
        Assert.Contains($"<PackageReference Include=\"System.Security.Cryptography.Xml\" Version=\"{expectedVersion}\"", project, StringComparison.Ordinal);
        Assert.Contains("ExcludeAssets=\"runtime\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmittedPackagedExecutablePinsServicedSecurityXmlDependency()
    {
        using var fixture = ArtifactFixture.Create("param([int] $Value); return $Value");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedPackagedSecurityDependency",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package)
        {
            EmitSource = true,
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var project = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "GeneratedPackagedSecurityDependency.csproj"));
        Assert.Contains("<PackageReference Include=\"System.Security.Cryptography.Xml\" Version=\"10.0.11\"", project, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RebuildWithoutEmitSourceRemovesPriorGeneratedProject()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-GeneratedProof { param([int] $Value) return $Value }");
        var firstSpec = new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict)
        {
            EmitSource = true
        };
        var first = new PowerShellCompilationArtifactBuilder().Build(firstSpec);
        Assert.True(first.Succeeded, first.Error + Environment.NewLine + first.BuildOutput);
        Assert.True(Directory.Exists(first.GeneratedSourcePath));

        var second = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "GeneratedProof",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict));

        Assert.True(second.Succeeded, second.Error + Environment.NewLine + second.BuildOutput);
        Assert.Null(second.GeneratedSourcePath);
        Assert.False(Directory.Exists(Path.Combine(fixture.OutputPath, "GeneratedProof.generated")));
    }
}
