namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativePatterns_PreserveCollectionResultsErrorsAndContinuation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-PatternFlow {
                [CmdletBinding()] param([ValidateNotNull()][string]$Mode, [object]$Value, [object]$Pattern)
                'before'
                if ($Mode -eq 'like') { $Value -like $Pattern }
                if ($Mode -eq 'clike') { $Value -clike $Pattern }
                if ($Mode -eq 'notlike') { $Value -notlike $Pattern }
                if ($Mode -eq 'cnotlike') { $Value -cnotlike $Pattern }
                if ($Mode -eq 'split') { $Value -split $Pattern }
                if ($Mode -eq 'csplit') { $Value -csplit $Pattern }
                'after'
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativePatterns", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Invoke-PatternFlow");
        Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        Assert.False(unit.RetainedHostedSource);
        const string probe = """
            function Describe-PatternRecord($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message}
                } else { [pscustomobject]@{type=$item.GetType().FullName;value=$item} }
            }
            $cases=@(
                @{value='ALPHA.beta';pattern='a*'}, @{value=@('ALPHA','alpha','beta');pattern='a*'},
                @{value=$null;pattern='*'}, @{value=@();pattern='*'},
                @{value='a.b.c';pattern='\.'}, @{value=@('A|B','a|b');pattern='a'},
                @{value='abc';pattern='['}, @{value='a.b.c';pattern=@('\.',2)},
                @{value='a.b.c';pattern=@('\.',-2)}, @{value='東京.beta';pattern='\.'})
            foreach ($mode in 'like','clike','notlike','cnotlike','split','csplit') {
                for ($index=0; $index -lt $cases.Count; $index++) {
                    foreach ($action in 'Continue','SilentlyContinue','Stop') {
                        $case=$cases[$index]; $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                        try { Invoke-PatternFlow -Mode $mode -Value $case.value -Pattern $case.pattern -ErrorAction $action -ErrorVariable faults 2>&1 |
                            ForEach-Object { $records.Add((Describe-PatternRecord $_)) } }
                        catch { $records.Add((Describe-PatternRecord $_)) }
                        [pscustomobject]@{mode=$mode;case=$index;action=$action;records=$records.ToArray();
                            faults=@($faults | ForEach-Object { Describe-PatternRecord $_ });
                            errors=@($Error | ForEach-Object { Describe-PatternRecord $_ })} | ConvertTo-Json -Depth 8 -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "patterns");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "patterns");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.Length >= 180);
        var differences = expected.Select((line, index) => (line, actual: actual[index], index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.Take(5)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
