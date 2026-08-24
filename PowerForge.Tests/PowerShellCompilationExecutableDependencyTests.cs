using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_PackagedExecutableBundlesReachableDotSourceClosureFromExplicitEntrypoint()
    {
        using var fixture = ArtifactFixture.Create(
            "param([string] $Name, [string] $DefaultRoot = $PSScriptRoot) " +
            ". \"$PSScriptRoot/Private/Get-Greeting.ps1\" -CallerRoot $PSScriptRoot; " +
            "\"default:$DefaultRoot\"; \"root:$PSScriptRoot\"; \"path:$PSCommandPath\"; " +
            "\"definition:$($MyInvocation.MyCommand.Definition)\"; " +
            "\"sidecar:$(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'settings.txt'))\"; " +
            "Get-Greeting -Name $Name; Get-CallerRoot");
        var privateDirectory = Path.Combine(fixture.RootPath, "Private");
        Directory.CreateDirectory(privateDirectory);
        var dependencyPath = Path.Combine(privateDirectory, "Get-Greeting.ps1");
        File.WriteAllText(
            dependencyPath,
            "param([string] $CallerRoot) function Get-Greeting { param([string] $Name) \"Hello, $Name from $PSScriptRoot\" }; function Get-CallerRoot { $CallerRoot }");
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
        File.WriteAllText(Path.Combine(fixture.OutputPath, "settings.txt"), "durable");
        var process = RunProcess(result.ArtifactPath!, "--Name=Ada");
        Assert.True(
            process.ExitCode == 0,
            $"Expected packaged dependency executable to succeed. Exit={process.ExitCode}; stdout={process.StandardOutput}; stderr={process.StandardError}");
        var lines = process.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        AssertPackagedPathsEqual(Path.GetDirectoryName(result.ArtifactPath!)!, lines[0].Substring("default:".Length));
        AssertPackagedPathsEqual(Path.GetDirectoryName(result.ArtifactPath!)!, lines[1].Substring("root:".Length));
        AssertPackagedPathsEqual(result.ArtifactPath!, lines[2].Substring("path:".Length));
        AssertPackagedPathsEqual(result.ArtifactPath!, lines[3].Substring("definition:".Length));
        Assert.Equal("sidecar:durable", lines[4]);
        Assert.True(
            process.StandardOutput.Contains("Hello, Ada from", StringComparison.Ordinal),
            $"Expected dependency output. Exit={process.ExitCode}; stderr={process.StandardError}; generated={File.ReadAllText(Directory.EnumerateFiles(result.GeneratedSourcePath!, "Source.ps1", SearchOption.AllDirectories).Single())}");
        Assert.Contains("Private", process.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetDirectoryName(result.ArtifactPath!)!, lines[5], StringComparison.OrdinalIgnoreCase);
        AssertPackagedPathsEqual(Path.GetDirectoryName(result.ArtifactPath!)!, lines[6]);
        Assert.True(string.IsNullOrWhiteSpace(process.StandardError), process.StandardError);
        Assert.Single(Directory.EnumerateFiles(result.GeneratedSourcePath!, "Dependency*.ps1", SearchOption.AllDirectories));
    }

    private static void AssertPackagedPathsEqual(string expected, string actual)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        Assert.True(
            string.Equals(Path.GetFullPath(expected), Path.GetFullPath(actual), comparison),
            $"Expected path '{expected}' but found '{actual}'.");
    }
}
