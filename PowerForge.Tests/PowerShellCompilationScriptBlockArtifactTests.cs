namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ScriptBlocks_ArtifactPreservesNestedScopeErrorsAndEscapedValues(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            $script:Marker='module'
            function Invoke-BlockFlow {
                [CmdletBinding()] param([string]$Mode)
                $trace=[Collections.Generic.List[string]]::new()
                $marker='outer'
                $block={
                    param([string]$Value)
                    $trace.Add($marker+':'+$Value)
                    $marker='child'
                    $nested={ param([string]$Text) $trace.Add($marker+':'+$Text); 'nested:'+ $Text }
                    try {
                        'before'
                        if ($Mode -eq 'parse') { [int]::Parse('bad') }
                        if ($Mode -eq 'throw') { throw 'block failure' }
                        if ($Mode -eq 'return') { return 'returned' }
                        & $nested $Value
                        'after'
                    } finally { $trace.Add('finally') }
                }
                & $block 'first'
                $marker='changed'
                & $block 'second'
                $trace -join '|'
                $marker
            }
            function Get-EscapedBlock {
                [CmdletBinding()] param([string]$Value)
                $block={ param([string]$Suffix) $script:Marker+':'+$Value+':'+$Suffix }
                $block
            }
            function Get-CapturedBlock {
                [CmdletBinding()] param([string]$Value)
                $block={ param([string]$Suffix) $Value+':'+$Suffix }
                $block.GetNewClosure()
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScriptBlocks", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Invoke-BlockFlow", "Get-EscapedBlock", "Get-CapturedBlock" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.False(unit.RetainedHostedSource);
            Assert.Single(unit.RegionGraph!.ScriptBlocks);
        }
        var flow = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, item => item.Name == "Invoke-BlockFlow");
        Assert.Single(Assert.Single(flow.RegionGraph!.ScriptBlocks).Graph.ScriptBlocks);
        const string probe = """
            function Describe-BlockRecord($item, [int]$depth=0) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message}
                } elseif ($null -eq $item) { [pscustomobject]@{type='null'} }
                elseif ($item -is [Exception]) { [pscustomobject]@{type=$item.GetType().FullName;message=$item.Message} }
                elseif ($item -is [Collections.IEnumerable] -and $item -isnot [string]) {
                    [pscustomobject]@{type=$item.GetType().FullName;items=@(if ($depth -lt 5) { foreach ($child in $item) { Describe-BlockRecord $child ($depth+1) } })}
                } else { [pscustomobject]@{type=$item.GetType().FullName;value=[string]$item} }
            }
            foreach ($mode in 'normal','parse','throw','return') {
                foreach ($action in 'Continue','SilentlyContinue','Stop') {
                    $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                    try { Invoke-BlockFlow -Mode $mode -ErrorAction $action -ErrorVariable faults 2>&1 |
                        ForEach-Object { $records.Add((Describe-BlockRecord $_)) } }
                    catch { $records.Add((Describe-BlockRecord $_)) }
                    [pscustomobject]@{mode=$mode;action=$action;records=$records.ToArray();
                        faults=@($faults | ForEach-Object { Describe-BlockRecord $_ });
                        errors=@($Error | ForEach-Object { Describe-BlockRecord $_ })} | ConvertTo-Json -Depth 8 -Compress
                }
            }
            $escaped=Get-EscapedBlock -Value 'expired'
            $Value='caller'
            [pscustomobject]@{escaped=@(& $escaped 'suffix');text=$escaped.ToString();
                parameters=@($escaped.Ast.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })} |
                ConvertTo-Json -Depth 5 -Compress
            $first=Get-CapturedBlock 'first'; $second=Get-CapturedBlock 'second'
            @(& $first 'one'; & $second 'two'; & $first 'three') | ConvertTo-Json -Compress
            'later:' + ((Invoke-BlockFlow -Mode 'return') -join ',')
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "scriptblocks");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "scriptblocks");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("later:", original.StandardOutput, StringComparison.Ordinal);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.True(expected.Length == actual.Length, "Original:" + Environment.NewLine + original.StandardOutput[..Math.Min(12000, original.StandardOutput.Length)] +
            Environment.NewLine + "Compiled:" + Environment.NewLine + compiled.StandardOutput[..Math.Min(12000, compiled.StandardOutput.Length)]);
        var differences = expected.Select((line, index) => (line, actual: actual[index], index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.Take(5)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
