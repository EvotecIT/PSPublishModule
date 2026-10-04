using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> CompleteHexWorkflowHosts()
        => StatementErrorHosts().Select(configuration => new[]
        {
            configuration[0], configuration[1], PowerShellCompilationMode.Hybrid
        });

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(CompleteHexWorkflowHosts))]
    public void CompleteWorkflow_PinnedHexPreservesCaptureErrorsAndContainers(string framework, string host, PowerShellCompilationMode mode)
    {
        var source = FindCompleteConversionWorkflow("PSSharedGoods", "Convert-HexToBinary.ps1");
        Assert.Equal("4ede6b109b121813150cdce0b515964127183c2feedd7e30d8d9c86737194a85",
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source))).ToLowerInvariant());
        using var fixture = ArtifactFixture.Create(File.ReadAllText(source), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.CompleteHex", PowerShellCompilationArtifactKind.BinaryModule,
            mode, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.NotNull(result.Manifest);
        Assert.Equal(1, result.Manifest.CompiledMethods);
        Assert.Equal(1, result.Manifest.RuntimeFallbackUnits);
        var unit = Assert.Single(result.Manifest.UnitDispositionLedger!.Entries, item => item.Name == "Convert-HexToBinary");
        Assert.True(unit.EmittedClrMethod);
        Assert.False(unit.RetainedHostedSource);
        Assert.Equal(1, unit.RuntimeCommandRegions); // Authored unqualified Write-Output keeps runtime command lookup.
        const string probe = """
            function Describe-HexRecord($item, [int]$depth=0) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message}
                } elseif ($item -is [Exception]) {
                    [pscustomobject]@{type=$item.GetType().FullName;message=$item.Message}
                } elseif ($null -eq $item) { [pscustomobject]@{type='null'} }
                else {
                    $children=@()
                    if ($depth -lt 3 -and $item -is [Collections.IEnumerable] -and $item -isnot [string]) {
                        $children=@(foreach ($child in $item) { Describe-HexRecord $child ($depth+1) })
                    }
                    [pscustomobject]@{type=$item.GetType().FullName;json=(ConvertTo-Json -InputObject $item -Depth 5 -Compress);children=$children}
                }
            }
            $cases=@(
                @{name='zero';value='00'}, @{name='max';value='FF'}, @{name='pair';value='00FF'},
                @{name='lowercase';value='7f80'}, @{name='invalid';value='GG'},
                @{name='invalid-first';value='GG00'}, @{name='invalid-middle';value='00GGFF'},
                @{name='invalid-last';value='00GG'}, @{name='odd';value='ABC'}, @{name='single';value='A'},
                @{name='empty';value=''}, @{name='null';value=$null}, @{name='numeric';value=10},
                @{name='nested';value=@(@('00','FF'),'7f')}, @{name='array';value=@('00','FF')})
            foreach ($case in $cases) {
                foreach ($binding in 'named','positional') {
                    foreach ($action in 'Continue','SilentlyContinue','Stop') {
                        $records=[Collections.Generic.List[object]]::new(); $faults=@(); $Error.Clear()
                        if ($action -eq 'Stop') {
                            try {
                                if ($binding -eq 'named') { Convert-HexToBinary -Hex $case.value -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                                else { Convert-HexToBinary $case.value -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                            } catch { [void]$records.Add((Describe-HexRecord $_)) }
                        } else {
                            if ($binding -eq 'named') { Convert-HexToBinary -Hex $case.value -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                            else { Convert-HexToBinary $case.value -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                        }
                        [pscustomobject]@{case=$case.name;binding=$binding;action=$action;records=$records.ToArray();
                            faults=@($faults | ForEach-Object { Describe-HexRecord $_ });
                            errors=@($Error | ForEach-Object { Describe-HexRecord $_ })} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            $pipelines=@(
                @{name='none';values=@()}, @{name='one';values=@('00')}, @{name='many';values=@('00','FF','7f80')},
                @{name='invalid-middle';values=@('00','GG','FF')}, @{name='binding-middle';values=@('00',$null,'FF')},
                @{name='nested-middle';values=@('00',@('AA','BB'),'FF')}, @{name='invalid-last';values=@('00','GG')})
            foreach ($pipeline in $pipelines) {
                foreach ($action in 'Continue','SilentlyContinue','Stop') {
                    foreach ($stopEarly in $false,$true) {
                        $records=[Collections.Generic.List[object]]::new(); $faults=@(); $Error.Clear()
                        if ($action -eq 'Stop') {
                            try {
                                if ($stopEarly) { $pipeline.values | Convert-HexToBinary -ErrorAction $action -ErrorVariable faults 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                                else { $pipeline.values | Convert-HexToBinary -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                            } catch { [void]$records.Add((Describe-HexRecord $_)) }
                        } else {
                            if ($stopEarly) { $pipeline.values | Convert-HexToBinary -ErrorAction $action -ErrorVariable faults 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                            else { $pipeline.values | Convert-HexToBinary -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object { [void]$records.Add((Describe-HexRecord $_)) } }
                        }
                        [pscustomobject]@{case=$pipeline.name;binding='pipeline';action=$action;stop=$stopEarly;records=$records.ToArray();
                            faults=@($faults | ForEach-Object { Describe-HexRecord $_ });
                            errors=@($Error | ForEach-Object { Describe-HexRecord $_ })} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            'later:' + ((Convert-HexToBinary -Hex '5A') -join ',')
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "complete-hex");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "complete-hex");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("later:90", original.StandardOutput, StringComparison.Ordinal);
        var originalLines = original.StandardOutput.Split('\n');
        var compiledLines = compiled.StandardOutput.Split('\n');
        Assert.Equal(originalLines.Length, compiledLines.Length);
        var differences = originalLines.Select((line, index) => (line, actual: compiledLines[index], index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.Take(12)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
