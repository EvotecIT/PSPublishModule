using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeUnary_PreservesPromotionConversionAndErrors(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-Positive { [CmdletBinding()] param([ValidateNotNull()][int]$Seed=1,[object]$Value); $result='before'; $result=+$Value; return ,$result }
            function Get-Negative { [CmdletBinding()] param([ValidateNotNull()][int]$Seed=1,[object]$Value); $result='before'; $result=-$Value; return ,$result }
            function Get-Complement { [CmdletBinding()] param([ValidateNotNull()][int]$Seed=1,[object]$Value); $result='before'; $result=-bnot $Value; return ,$result }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeUnary", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries.Where(unit => unit.Name.StartsWith("Get-", StringComparison.Ordinal)),
            unit => { Assert.True(unit.EmittedClrMethod); Assert.False(unit.RetainedHostedSource); });
        const string probe = """
            $cases=@($null,0,1,[int]::MinValue,[long]::MinValue,[uint32]::MaxValue,[uint64]::MaxValue,
                [single]1.25,[double]::NaN,[double]::PositiveInfinity,[decimal]::MinValue,
                '12','1.5','bad',$true,[char]'A',@(),@(1),@(1,2),[pscustomobject]@{Value=1})
            foreach ($name in 'Get-Positive','Get-Negative','Get-Complement') {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    for ($i=0; $i -lt $cases.Count; $i++) {
                        $Error.Clear(); $faults=@(); $caught=$null; $records=@()
                        try { $records=@(& $name -Value $cases[$i] -ErrorAction $action -ErrorVariable faults 2>$null) }
                        catch { $caught=$_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName }
                        [pscustomobject]@{name=$name;case=$i;action=$action;
                            records=@($records | ForEach-Object { if ($null -eq $_) { 'null' } else { $_.GetType().FullName+':'+[string]$_ } });
                            caught=$caught; faults=@($faults | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName });
                            errors=@($Error | ForEach-Object { $_.FullyQualifiedErrorId+':'+$_.Exception.GetType().FullName })} | ConvertTo-Json -Compress -Depth 6
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-unary");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-unary");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Equal(0, compiled.ExitCode);
        Assert.Empty(compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(240, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }
}
