using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCurrentReviewRegressionTests
{
    [Fact]
    public void Build_StrictExecutableIncludesAdvancedCommonParametersInAbbreviationResolution()
    {
        using var fixture = ArtifactFixture.Create("[CmdletBinding()] param([string] $Value); return $Value");
        var result = BuildExecutable(fixture, "PowerForge.StrictCommonParameters", PowerShellCompilationMode.Strict);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var ambiguous = Run(result.ArtifactPath!, "-V", "Ada");
        Assert.NotEqual(0, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError, StringComparison.OrdinalIgnoreCase);

        var exactCommon = Run(result.ArtifactPath!, "-Verbose");
        Assert.NotEqual(0, exactCommon.ExitCode);
        Assert.Contains("common parameter", exactCommon.StandardError, StringComparison.OrdinalIgnoreCase);

        var exactAuthored = Run(result.ArtifactPath!, "-Value", "Ada");
        Assert.Equal(0, exactAuthored.ExitCode);
        Assert.Equal("Ada", exactAuthored.StandardOutput.Trim());
    }

    [Fact]
    public void Build_PackagedExecutableIncludesNonSwitchCommonParametersInAbbreviationResolution()
    {
        using var fixture = ArtifactFixture.Create("[CmdletBinding()] param([string] $ErrorCode); return $ErrorCode");
        var result = BuildExecutable(fixture, "PowerForge.PackageCommonParameters", PowerShellCompilationMode.Package);

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var ambiguous = Run(result.ArtifactPath!, "-E", "Stop");
        Assert.NotEqual(0, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError, StringComparison.OrdinalIgnoreCase);

        var exact = Run(result.ArtifactPath!, "-ErrorCode", "Stop");
        Assert.Equal(0, exact.ExitCode);
        Assert.Equal("Stop", exact.StandardOutput.Trim());
    }

    [Fact]
    public void Resolve_DefaultOutputAvoidsRecursiveLoaderAndExplicitOverlapFailsBeforePublication()
    {
        using var fixture = ArtifactFixture.Create(
            "$Files = @(Get-ChildItem -Path \"$PSScriptRoot/*.ps1\" -Recurse); foreach ($File in $Files) { . $File.FullName }; Export-ModuleMember -Function Get-One",
            ".psm1");
        File.WriteAllText(Path.Combine(fixture.RootPath, "Get-One.ps1"), "function Get-One { return 1 }");
        var resolved = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);

        var defaultOutput = PowerShellCompilationOutputPolicy.GetDefaultOutputDirectory(resolved);
        Assert.False(defaultOutput.StartsWith(
            fixture.RootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));

        Directory.CreateDirectory(Path.Combine(defaultOutput, "Generated"));
        File.WriteAllText(Path.Combine(defaultOutput, "Generated", "Generated.ps1"), "function Get-Generated { return 2 }");
        var secondResolution = new PowerShellCompilationInputResolver().Resolve(fixture.RootPath);
        Assert.DoesNotContain(secondResolution.SourceFiles, path => path.Contains("Generated.ps1", StringComparison.OrdinalIgnoreCase));

        var unsafeOutput = Path.Combine(fixture.RootPath, "artifacts");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                resolved.SourcePath,
                unsafeOutput,
                "PowerForge.RecursiveLoaderOverlap",
                resolved.Kind,
                resolved.Mode)
            {
                CompilationSourcePaths = resolved.CompilationSourceFiles,
                ModuleManifestPath = resolved.ModuleManifestPath
            }));
        Assert.Contains("recursive conventional loader root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(unsafeOutput));
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsMandatoryNullCollectionsAndHonorsAllowNull()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-RequiredLength { [CmdletBinding()] param([Parameter(Mandatory)] [int[]] $Values) return $Values.Length } " +
            "function Get-AllowedLength { [CmdletBinding()] param([Parameter(Mandatory)] [AllowNull()] [int[]] $Values) return $Values.Length } " +
            "function Invoke-RequiredLength { return Get-RequiredLength -Values $null } " +
            "function Invoke-AllowedLength { return Get-AllowedLength -Values $null } " +
            "Export-ModuleMember -Function Invoke-RequiredLength, Invoke-AllowedLength",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.MandatoryCollection",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict));

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; try {{ Invoke-RequiredLength; 'unexpected' }} catch {{ 'required=' + $_.Exception.Message }}; 'allowed=' + (Invoke-AllowedLength)");
        Assert.Equal(0, run.ExitCode);
        Assert.Contains("required=Mandatory parameter '-Values' does not allow null values.", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("allowed=0", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("unexpected", run.StandardOutput, StringComparison.Ordinal);
    }

    private static PowerShellCompilationBuildResult BuildExecutable(
        ArtifactFixture fixture,
        string name,
        PowerShellCompilationMode mode)
        => new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            name,
            PowerShellCompilationArtifactKind.Executable,
            mode)
        {
            SingleFile = false,
            EmitSource = true
        });

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Process '{fileName}' did not exit within 120 seconds.");
        }
        return (process.ExitCode, stdout, stderr);
    }

    private sealed class ArtifactFixture : IDisposable
    {
        private ArtifactFixture(string rootPath, string scriptPath, string outputPath)
        {
            RootPath = rootPath;
            ScriptPath = scriptPath;
            OutputPath = outputPath;
        }

        internal string RootPath { get; }

        internal string ScriptPath { get; }

        internal string OutputPath { get; }

        internal static ArtifactFixture Create(string source, string extension = ".ps1")
        {
            var root = Path.Combine(Path.GetTempPath(), "PowerForgeCompilationReview-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var script = Path.Combine(root, "Source" + extension);
            File.WriteAllText(script, source);
            return new ArtifactFixture(root, script, Path.Combine(root, "output"));
        }

        public void Dispose()
        {
            try { Directory.Delete(RootPath, recursive: true); } catch { }
            var siblingArtifacts = Path.Combine(
                Directory.GetParent(RootPath)?.FullName ?? RootPath,
                "artifacts",
                new DirectoryInfo(RootPath).Name);
            try { Directory.Delete(siblingArtifacts, recursive: true); } catch { }
        }
    }
}
