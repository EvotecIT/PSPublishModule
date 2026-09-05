using System.Diagnostics;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
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
                Microsoft.PowerShell.Utility\Write-Verbose -Message 'verbose-record'
                Microsoft.PowerShell.Utility\Write-Debug -Message 'debug-record'
                Microsoft.PowerShell.Utility\Write-Warning -Message 'warning-record'
                Microsoft.PowerShell.Utility\Write-Output 'region-one'
                Microsoft.PowerShell.Utility\Write-Output $Name
                return $Name
            }

            function Get-PipelineIsland {
                [CmdletBinding()]
                param([string] $Name)
                $Name | Microsoft.PowerShell.Core\ForEach-Object { $_.ToUpperInvariant() }
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
            Assert.Equal(2, binaryModulePlan.CompilableUnits);

            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                source,
                output,
                "PowerForge.StreamIslands",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0"
            });
            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            Assert.Equal(2, result.Manifest!.CompiledMethods);
            Assert.Equal(2, result.Manifest.RuntimeFallbackUnits);

            var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
            var invocation = $"$DebugPreference='Continue'; Import-Module -Name '{escapedPath}' -Force; Get-IslandValue -Name Ada -Verbose; Get-PipelineIsland -Name Ada";
            var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", invocation);
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            Assert.Contains("VERBOSE: verbose-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("DEBUG: debug-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("WARNING: warning-record", run.StandardOutput, StringComparison.Ordinal);
            Assert.Contains("region-one", run.StandardOutput, StringComparison.Ordinal);
            Assert.Equal(2, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Count(line => line == "Ada"));
            Assert.Contains("ADA", run.StandardOutput, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Analyze_CommandRegionRejectsVariablesHiddenInsideNestedScriptBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCommandIslands", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "UnsafeClosure.psm1");
        File.WriteAllText(
            source,
            "function Get-UnsafeClosure { $Uri = 'https://example.test'; Invoke-Thing -ScriptBlock { $Uri } }");

        try
        {
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                source,
                PowerShellCompilationMode.Hybrid,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapability.PowerShellStreams));

            var unit = Assert.Single(Assert.Single(plan.Files).Units);
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Build_StrictBinaryModuleCoalescesConditionalAndAdjacentCommandRegions()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCommandIslands", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var source = Path.Combine(root, "CoarseIsland.psm1");
        File.WriteAllText(
            source,
            "function Get-CoarseIsland { [CmdletBinding()] param([string] $Name, [bool] $Upper); " +
            "if ($Upper) { Write-Output $Name.ToUpperInvariant() } else { Write-Output $Name }; " +
            "Write-Output 'tail-region'; return $Name }");

        try
        {
            var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                source,
                output,
                "PowerForge.CoarseCommandIsland",
                PowerShellCompilationArtifactKind.BinaryModule,
                PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = "net10.0",
                EmitSource = true
            });

            Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
            var generated = File.ReadAllText(Path.Combine(result.GeneratedSourcePath!, "CompiledPowerShell.cs"));
            Assert.Equal(1, generated.Split(new[] { "__invokePowerShellRegion(\"" }, StringSplitOptions.None).Length - 1);
            var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
            var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", $"Import-Module '{escapedPath}' -Force; Get-CoarseIsland -Name Ada -Upper $true");
            Assert.True(run.ExitCode == 0, run.StandardError + Environment.NewLine + run.StandardOutput);
            Assert.Equal(new[] { "ADA", "tail-region", "Ada" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Analyze_CommandRegionRejectsTopLevelPipelineAutomaticVariable()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCommandIslands", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "UnsafeAutomaticVariable.psm1");
        File.WriteAllText(source, "function Get-UnsafeAutomaticVariable { Write-Output $_ }");

        try
        {
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                source,
                PowerShellCompilationMode.Hybrid,
                targetFramework: "net10.0",
                capabilities: PowerShellCompilationCapability.PowerShellStreams));

            var unit = Assert.Single(Assert.Single(plan.Files).Units);
            Assert.False(unit.IsCompilable);
            Assert.Contains(unit.Diagnostics, diagnostic =>
                diagnostic.Code == PowerShellCompilationDiagnosticCode.CommandInvocation);
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
