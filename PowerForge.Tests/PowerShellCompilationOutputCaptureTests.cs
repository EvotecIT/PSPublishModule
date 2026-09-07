using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Values=for ([int]$i=0;$i -lt 2;$i++) { return $i }")]
    [InlineData("[int]$Values=0; $Values=for ([int]$i=0;$i -lt 2;$i++) { $i }")]
    [InlineData("$script:Values=for ([int]$i=0;$i -lt 2;$i++) { $i }")]
    [InlineData("$Values=for ([int]$i=0;$i -lt 2;$i++) { $Values='x'; $i }")]
    [InlineData("$Values=for ([int]$i=0;$i -lt 2;$i++) { [string]$Values='x'; $i }")]
    [InlineData("$Values=foreach ($Values in 1,2) { $Values }")]
    [InlineData("[int]$i=0; $Values=while ($i -lt 2) { $Values='x'; $i; $i++ }")]
    [InlineData("[int]$i=0; $Values=do { $Values='x'; $i; $i++ } while ($i -lt 2)")]
    [InlineData("[int]$i=0; $Values=do { [string]$Values='x'; $i; $i++ } until ($i -eq 2)")]
    public void OutputCapture_RetainsUnqualifiedTransfersAndTargets(string body)
    {
        using var fixture = ArtifactFixture.Create("function Get-Values { [CmdletBinding()] param(); " + body + "; 'after' }", ".psm1");
        var typed = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CaptureBoundary", "net10.0");
        Assert.Empty(typed.Methods);
        Assert.NotEmpty(typed.Diagnostics);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("net10.0", "pwsh")]
    [InlineData("net8.0", "pwsh")]
    public void OutputCapture_PreservesNestedNullAndFlowRecords(string framework, string host)
    {
        if (framework == "net8.0") host = Environment.GetEnvironmentVariable("POWERFORGE_PWSH74_PATH") ?? host;
        using var fixture = ArtifactFixture.Create("""
            function Get-NestedCapture {
                [CmdletBinding()] param()
                $Values=for ([int]$i=0;$i -lt 2;$i++) {
                    $Inner=for ([int]$j=0;$j -lt 2;$j++) { $j }
                    ,$Inner
                }
                'after'
                return ,$Values
            }
            function Get-NullCapture {
                [CmdletBinding()] param()
                $Values=foreach ($item in 1,2) { ,$null }
                'after'
                return ,$Values
            }
            function Get-FlowCapture {
                [CmdletBinding()] param()
                $Values=for ([int]$i=0;$i -lt 6;$i++) {
                    if ($i -eq 1) { continue }
                    if ($i -eq 4) { break }
                    $i
                }
                'after'
                return ,$Values
            }
            function Get-Pair { [CmdletBinding()] param() 'first'; return 'second' }
            function Get-WhileCapture {
                [CmdletBinding()] param()
                [int]$i=0
                $Values=while ($i -lt 3) { $i; $i++ }
                'after'
                return ,$Values
            }
            function Get-DoCapture {
                [CmdletBinding()] param()
                [int]$i=0
                $Values=do { $i; $i++ } until ($i -eq 3)
                'after'
                return ,$Values
            }
            function Get-CallCapture {
                [CmdletBinding()] param()
                $Values=foreach ($item in 1,2) { Get-Pair }
                'after'
                return ,$Values
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NestedOutputCapture", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(7, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($name in 'Get-NestedCapture','Get-NullCapture','Get-FlowCapture','Get-CallCapture','Get-WhileCapture','Get-DoCapture') {
                $records=@(& $name)
                [pscustomobject]@{name=$name;count=$records.Count;records=$records} | ConvertTo-Json -Depth 10 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-nested-capture");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-nested-capture");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.True(original.StandardOutput == compiled.StandardOutput, "Original:\n" + original.StandardOutput + "\nCompiled:\n" + compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void OutputCapture_PreservesLoopRecordCollapse(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-CapturedValues {
                [CmdletBinding()] param([int]$Count)
                $Values=for ([int]$Index=0; $Index -lt $Count; $Index++) { $Index }
                'after'
                return ,$Values
            }
            function Test-CapturedCollection {
                [CmdletBinding()] param([int]$Count)
                $Values=for ([int]$Index=0; $Index -lt $Count; $Index++) { $Index }
                'after'
                return $Values -is [object[]]
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.OutputCapture", PowerShellCompilationArtifactKind.BinaryModule,
            framework == "net472" ? PowerShellCompilationMode.Hybrid : PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(framework == "net472" ? 1 : 2, result.Manifest!.CompiledMethods);
        Assert.Equal(framework == "net472" ? 1 : 0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            foreach ($count in 0,1,3) {
                $records=@(Get-CapturedValues $count)
                $values=$records[1]
                [pscustomobject]@{ count=$count; records=$records.Count; first=$records[0];
                    type=$(if ($null -eq $values) { 'null' } else { $values.GetType().FullName }); values=$values } | ConvertTo-Json -Compress
                @(Test-CapturedCollection $count) | ConvertTo-Json -Compress
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-output-capture");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-output-capture");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.True(original.StandardOutput == compiled.StandardOutput,
            "Original:" + Environment.NewLine + original.StandardOutput + Environment.NewLine + "Compiled:" + Environment.NewLine + compiled.StandardOutput);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
