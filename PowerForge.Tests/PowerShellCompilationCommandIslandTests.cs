using System.Diagnostics;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCommandIslandTests
{
    [Fact]
    public void Build_StrictBinaryModuleCompilesTypedRegionAroundPowerShellStreamIslands()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCommandIslands", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var source = Path.Combine(root, "StreamIslands.psm1");
        File.WriteAllText(
            source,
            """
            function Get-IslandValue {
                [CmdletBinding()]
                param([string] $Name)
                Write-Verbose -Message 'verbose-record'
                Write-Debug -Message 'debug-record'
                Write-Warning -Message 'warning-record'
                Write-Output 'region-one'
                Write-Output $Name
                return $Name
            }
            """);

        try
        {
            var runtimeIndependent = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(source));
            Assert.Equal(0, runtimeIndependent.CompilableUnits);

            var binaryModulePlan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                source,
                PowerShellCompilationMode.Strict,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapability.PowerShellStreams));
            Assert.Equal(1, binaryModulePlan.CompilableUnits);

            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                source,
                output,
                "PowerForge.StreamIslands",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Strict)
            {
                TargetFramework = "net10.0"
            });
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.Equal(1, result.Manifest!.CompiledMethods);
            Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);

            var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
            var invocation = $"$DebugPreference='Continue'; Import-Module -Name '{escapedPath}' -Force; Get-IslandValue -Name Ada -Verbose";
            var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", invocation);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            Assert.Contains("VERBOSE: verbose-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("DEBUG: debug-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("WARNING: warning-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("region-one", run.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(2, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(line => line == "Ada"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static (int ExitCode, string StandardOutput, string StandardError) Run(string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "PowerShell stream-island process timed out.");
        return (process.ExitCode, standardOutput, standardError);
    }
}
