using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> BackgroundPipelineHosts()
        => StatementErrorHosts().Where(static configuration => !Equals(configuration[0], "net472"));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(BackgroundPipelineHosts))]
    public void BackgroundPipelines_PreserveJobsAcrossCommandRegionAndIntrinsicRoutes(string framework, string host)
    {
        var bodies = new[]
        {
            "Write-Output 'background' | ForEach-Object { $_ } &",
            "Microsoft.PowerShell.Utility\\Write-Output 'background' &",
            "[object]$job=Write-Output 'background' | ForEach-Object { $_ } &; return $job"
        };
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Invoke-LegacyBackground" + index + " { [CmdletBinding()] param([int]$Value=3); $copy=$Value; $copy*=2; $copy; " + body + " }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.BackgroundPipelines", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        var units = result.Manifest.UnitDispositionLedger!.Entries;
        Assert.All(units, unit =>
        {
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        var constrained = Assert.Single(units, unit => unit.Name == "Invoke-LegacyBackground2");
        Assert.True(constrained.EmittedClrMethod);
        Assert.False(constrained.RetainedHostedSource);
        const string probe = """
            foreach($shape in 0,1,2) {
                $name='Invoke-LegacyBackground'+$shape
                $observed=@(& $name)
                $jobs=@($observed | Where-Object { $_ -is [System.Management.Automation.Job] })
                $records=@($observed | Where-Object { $_ -isnot [System.Management.Automation.Job] })
                $received=@()
                if($jobs.Count -gt 0) {
                    try { $received=@($jobs | Wait-Job -Timeout 20 | Receive-Job -ErrorAction Stop) }
                    finally { $jobs | Remove-Job -Force }
                }
                [pscustomobject]@{shape=$shape;jobs=$jobs.Count;records=$records;received=$received} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "background-routes-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "background-routes-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + "\nGenerated: " + compiled.StandardOutput + "\nErrors: " + compiled.StandardError);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [InlineData("Microsoft.PowerShell.Utility\\Write-Output 'background' &", "background")]
    [InlineData("('suppressed') >$null", "redirection")]
    public void PipelineOperators_RequireAnExplicitNativeInvocationContract(string body, string diagnostic)
    {
        using var fixture = ArtifactFixture.Create("function Invoke-Operator { [CmdletBinding()] param(); " + body + " }", ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.BackgroundStrict", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true));
        Assert.False(result.Succeeded);
        Assert.Contains(diagnostic, result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PipelineRedirections_PreserveExpressionAndIntrinsicSuppression(string framework, string host)
    {
        var bodies = new[]
        {
            "('suppressed') >$null; 'after'",
            "$copy='before'; $copy=('suppressed') >$null; $copy; 'after'",
            "Microsoft.PowerShell.Utility\\Write-Output 'suppressed' >$null; 'after'",
            "$copy=@('before'; ('suppressed') >$null; 'after'); return $copy"
        };
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Invoke-PipelineRedirection" + index + " { [CmdletBinding()] param([int]$Value=3); " + body + " }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.PipelineRedirections", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(bodies.Length, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit =>
        {
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        const string probe = """
            foreach($shape in 0,1,2,3) {
                $name='Invoke-PipelineRedirection'+$shape
                [pscustomobject]@{shape=$shape;records=@(& $name)} | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "redirection-routes-original");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "redirection-routes-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original: " + original.StandardOutput + "\nGenerated: " + compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
