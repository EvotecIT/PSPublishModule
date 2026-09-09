namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ScriptBlocks_ArtifactIsolatesUnrelatedNewerLifecycleSyntax(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-SupportedBlock {
                $block={ param([string]$Value) 'result:'+ $Value }
                $block
            }
            function Get-NewLifecycle {
                [CmdletBinding()] param()
                begin { 'begin' }
                clean { 'discarded' }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScriptBlockHostIsolation", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(2, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        var run = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'; $block=Get-SupportedBlock; & $block 'ready'", fixture.RootPath, "scriptblock-host-isolation");
        Assert.True(run.ExitCode == 0, run.StandardOutput + run.StandardError);
        Assert.Equal("result:ready", run.StandardOutput.Trim());
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ScriptBlocks_ArtifactRetainsOwnerWhenChildFailsLowering(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Block {
                $block={ param($__writeOutput) $__writeOutput; 'after' }
                $block
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScriptBlockFallback", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries);
        Assert.True(unit.RetainedHostedSource);
        Assert.False(unit.EmittedClrMethod);
        const string probe = "$block=Get-Block; @(& $block 'ready'; & $block 'again') | ConvertTo-Json -Compress";
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "scriptblock-fallback");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "scriptblock-fallback");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("ready", original.StandardOutput);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
