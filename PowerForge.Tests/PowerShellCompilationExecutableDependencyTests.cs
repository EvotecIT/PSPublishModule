using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_PackagedExecutableBundlesReachableDotSourceClosureFromExplicitEntrypoint()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Name) . \"$PSScriptRoot/Private/Get-Greeting.ps1\"; Get-Greeting -Name $Name");
        var privateDirectory = Path.Combine(fixture.RootPath, "Private");
        Directory.CreateDirectory(privateDirectory);
        var dependencyPath = Path.Combine(privateDirectory, "Get-Greeting.ps1");
        File.WriteAllText(dependencyPath, "function Get-Greeting { param([string] $Name) \"Hello, $Name from $PSScriptRoot\" }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(
            new[] { fixture.ScriptPath, dependencyPath },
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package,
            fixture.ScriptPath);
        var spec = new PowerShellCompilationBuildSpec(
            resolved.SourcePath,
            fixture.OutputPath,
            "PowerForge.DependencyBundleProof",
            resolved.Kind,
            resolved.Mode)
        {
            CompilationSourcePaths = resolved.CompilationSourceFiles,
            EmitSource = true
        };

        var result = new PowerShellCompilationArtifactBuilder().Build(spec);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.SourceFiles.Length);
        var process = RunProcess(result.ArtifactPath!, "--Name=Ada");
        Assert.Equal(0, process.ExitCode);
        Assert.True(
            process.StandardOutput.Contains("Hello, Ada from", StringComparison.Ordinal),
            $"Expected dependency output. Exit={process.ExitCode}; stderr={process.StandardError}; generated={File.ReadAllText(Directory.EnumerateFiles(result.GeneratedSourcePath!, "Source.ps1", SearchOption.AllDirectories).Single())}");
        Assert.Contains("Private", process.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrWhiteSpace(process.StandardError), process.StandardError);
        Assert.Single(Directory.EnumerateFiles(result.GeneratedSourcePath!, "Dependency*.ps1", SearchOption.AllDirectories));
    }
}
