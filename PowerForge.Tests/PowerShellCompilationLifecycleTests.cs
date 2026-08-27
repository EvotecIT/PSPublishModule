using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    public void Build_HybridAdvancedFunctionRunsBeginProcessEndAndCleanPerPipeline()
    {
        using var fixture = ArtifactFixture.Create(
            """
            function Invoke-Lifecycle {
                [CmdletBinding(SupportsShouldProcess, ConfirmImpact='Low')]
                param(
                    [Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName, Position=0)]
                    [Alias('Value')]
                    [int] $Number,
                    [Parameter(ValueFromRemainingArguments)]
                    [string[]] $Remaining,
                    [string] $CleanupPath
                )
                begin { $total = 0 }
                process {
                    Write-Progress -Activity 'Lifecycle' -Status "Number $Number"
                    if ($Number -lt 0) { Write-Error "negative:$Number"; return }
                    if ($Number -eq 99) { throw 'terminating:99' }
                    if ($PSCmdlet.ShouldProcess("number:$Number", 'Accumulate')) {
                        $total += $Number
                        $total
                        if ($Remaining.Count -gt 0) { "remaining:$($Remaining -join ',')" }
                    }
                }
                end { "end:$total" }
                clean {
                    if ($CleanupPath) { [System.IO.File]::WriteAllText($CleanupPath, 'cleaned') }
                }
            }
            Export-ModuleMember -Function Invoke-Lifecycle
            """,
            ".psm1");
        var cleanup = Path.Combine(fixture.RootPath, "cleaned.txt");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.Lifecycle",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid)
        {
            TargetFramework = "net10.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        var lifecycle = Assert.Single(result.Manifest.Lifecycles);
        Assert.Equal(PowerShellCompilationLifecycleExecution.HostedSteppablePipeline, lifecycle.Execution);
        Assert.True(lifecycle.HasBegin && lifecycle.HasProcess && lifecycle.HasEnd && lifecycle.HasClean);
        Assert.True(lifecycle.ValueFromPipeline);
        Assert.True(lifecycle.ValueFromPipelineByPropertyName);
        Assert.True(lifecycle.ValueFromRemainingArguments);
        Assert.True(lifecycle.CommonParameters);
        Assert.True(lifecycle.SupportsShouldProcess);
        Assert.Equal("Low", lifecycle.ConfirmImpact, ignoreCase: true);

        var escapedCleanup = cleanup.Replace("'", "''", StringComparison.Ordinal);
        var proof = RunModuleProof(
            result.ArtifactPath!,
            "$command = Get-Command Invoke-Lifecycle; " +
            "\"$($command.CommandType)|$($command.Parameters.ContainsKey('Verbose'))|$($command.Parameters.ContainsKey('WhatIf'))\"; " +
            $"1,2 | Invoke-Lifecycle -CleanupPath '{escapedCleanup}' -Confirm:$false");
        Assert.Equal(new[] { "Cmdlet|True|True", "1", "3", "end:3" }, proof.Split(Environment.NewLine));
        Assert.Equal("cleaned", File.ReadAllText(cleanup));

        var byProperty = RunModuleProof(
            result.ArtifactPath!,
            "[pscustomobject]@{ Number = 4 } | Invoke-Lifecycle -Confirm:$false");
        Assert.Equal(new[] { "4", "end:4" }, byProperty.Split(Environment.NewLine));

        var remaining = RunModuleProof(
            result.ArtifactPath!,
            "Invoke-Lifecycle 2 a b -Confirm:$false");
        Assert.Equal(new[] { "2", "remaining:a,b", "end:2" }, remaining.Split(Environment.NewLine));

        var nonTerminating = RunModuleProof(
            result.ArtifactPath!,
            "-1,2 | Invoke-Lifecycle -ErrorAction SilentlyContinue -Confirm:$false");
        Assert.Equal(new[] { "2", "end:2" }, nonTerminating.Split(Environment.NewLine));

        var whatIf = RunModuleProof(
            result.ArtifactPath!,
            "$result = 2 | Invoke-Lifecycle -WhatIf; $result[-1]");
        Assert.Contains("What if:", whatIf, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("0", whatIf, StringComparison.Ordinal);

        File.Delete(cleanup);
        var terminating = RunModuleProof(
            result.ArtifactPath!,
            $"try {{ 99 | Invoke-Lifecycle -CleanupPath '{escapedCleanup}' -Confirm:$false }} catch {{ 'caught:' + $_.Exception.Message }}; Test-Path '{escapedCleanup}'");
        Assert.Contains("terminating:99", terminating, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("True", terminating, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StrictBinaryModuleRejectsHostedLifecycle()
    {
        using var fixture = ArtifactFixture.Create(
            "function Invoke-Lifecycle { [CmdletBinding()] param([Parameter(ValueFromPipeline)][int]$Number) begin { $total = 0 } process { $total += $Number } end { $total } }",
            ".psm1");

        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.StrictLifecycle",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict)
        {
            TargetFramework = "net10.0"
        });

        Assert.False(result.Succeeded);
        Assert.Contains("begin", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Strict", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
