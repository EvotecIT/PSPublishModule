namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativePatternSourceHosts))]
    public void NativeMatchAndRange_PreserveHostSemanticsErrorsStateAndEvaluation(
        string framework,
        string host,
        string lineEnding)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-MatchFlow {
                [CmdletBinding()] param([object]$Value, [object]$Pattern, [switch]$CaseSensitive, [switch]$Negate)
                'before'
                $null = 'seed-9' -match '(?<word>seed)-(?<digit>9)'
                if ($CaseSensitive) {
                    if ($Negate) { $result = $Value -cnotmatch $Pattern } else { $result = $Value -cmatch $Pattern }
                } else {
                    if ($Negate) { $result = $Value -notmatch $Pattern } else { $result = $Value -match $Pattern }
                }
                [pscustomobject]@{
                    Result = $result
                    Match0 = $Matches[0]
                    Word = $Matches['word']
                    Digit = $Matches['digit']
                    MatchCount = $Matches.Count
                }
                'after'
            }
            function Invoke-RangeFlow {
                [CmdletBinding()] param([object]$Left, [object]$Right)
                'before'
                $Left..$Right
                'after'
            }
            function Invoke-RangeOnce {
                [CmdletBinding()] param()
                $reads = 0
                (++$reads)..(++$reads)
                "reads=$reads"
            }
            function Invoke-RangeFailureOrder {
                [CmdletBinding()] param()
                $reads = 0
                try { 'bad'..(++$reads) } catch { "error=$($_.FullyQualifiedErrorId)" }
                "reads=$reads"
            }
            function Invoke-RangeSlice {
                [CmdletBinding()] param([object]$Values, [object]$Left, [object]$Right)
                $Values[$Left..$Right]
            }
            function Invoke-RangeIterator {
                [CmdletBinding()] param([object]$Left, [object]$Right)
                foreach ($item in $Left..$Right) { $item; break }
            }
            """.ReplaceLineEndings(lineEnding), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeMatchRange", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Invoke-MatchFlow", "Invoke-RangeFlow", "Invoke-RangeOnce", "Invoke-RangeFailureOrder", "Invoke-RangeSlice", "Invoke-RangeIterator" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        }

        const string probe = """
            function Describe-Record($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message;
                        line=$item.InvocationInfo.ScriptLineNumber;column=$item.InvocationInfo.OffsetInLine;source=$item.InvocationInfo.Line}
                } elseif ($null -eq $item) { [pscustomobject]@{type='null';value=$null} }
                else { [pscustomobject]@{type=$item.GetType().FullName;value=$item} }
            }
            $matchCases=@(
                @{name='scalar';value='Alpha-42';pattern='(?<word>[a-z]+)-(\d+)';culture=$null},
                @{name='collection';value=@('Alpha-42','none','beta-7');pattern='(?<word>[a-z]+)-(\d+)';culture=$null},
                @{name='regex-object';value='Alpha-42';pattern=[regex]::new('(?<word>[a-z]+)-(\d+)',[Text.RegularExpressions.RegexOptions]::IgnoreCase);culture=$null},
                @{name='turkish';value='I';pattern='ı';culture='tr-TR'},
                @{name='conversion';value=123;pattern='2';culture=$null},
                @{name='null';value=$null;pattern='.';culture=$null},
                @{name='invalid';value='abc';pattern='[';culture=$null})
            foreach ($case in $matchCases) {
                $previousCulture=[Globalization.CultureInfo]::CurrentCulture
                if ($case.culture) { [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($case.culture) }
                foreach ($caseSensitive in $false,$true) {
                    foreach ($negate in $false,$true) {
                        foreach ($action in 'Continue','Stop') {
                            $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                            try { Invoke-MatchFlow -Value $case.value -Pattern $case.pattern -CaseSensitive:$caseSensitive -Negate:$negate -ErrorAction $action -ErrorVariable faults 2>&1 |
                                ForEach-Object { $records.Add((Describe-Record $_)) } }
                            catch { $records.Add((Describe-Record $_)) }
                            [pscustomobject]@{kind='match';case=$case.name;caseSensitive=$caseSensitive;negate=$negate;action=$action;
                                records=$records.ToArray();faults=@($faults | ForEach-Object { Describe-Record $_ });
                                errors=@($Error | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 9 -Compress
                        }
                    }
                }
                [Globalization.CultureInfo]::CurrentCulture=$previousCulture
            }
            $rangeCases=@(
                @{name='ascending';left=1;right=3}, @{name='descending';left=3;right=1},
                @{name='single';left=1;right=1}, @{name='strings';left='1';right='3'},
                @{name='fraction';left=1.8;right=3.2}, @{name='null';left=$null;right=2},
                @{name='chars';left=[char]'a';right=[char]'c'}, @{name='invalid';left='bad';right=2},
                @{name='overflow';left=[long]2147483648;right=[long]2147483649})
            foreach ($case in $rangeCases) {
                foreach ($action in 'Continue','Stop') {
                    $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                    try { Invoke-RangeFlow -Left $case.left -Right $case.right -ErrorAction $action -ErrorVariable faults 2>&1 |
                        ForEach-Object { $records.Add((Describe-Record $_)) } }
                    catch { $records.Add((Describe-Record $_)) }
                    [pscustomobject]@{kind='range';case=$case.name;action=$action;records=$records.ToArray();
                        faults=@($faults | ForEach-Object { Describe-Record $_ });errors=@($Error | ForEach-Object { Describe-Record $_ })} |
                        ConvertTo-Json -Depth 9 -Compress
                }
            }
            [pscustomobject]@{kind='once';records=@(Invoke-RangeOnce)} | ConvertTo-Json -Depth 5 -Compress
            [pscustomobject]@{kind='failure-order';records=@(Invoke-RangeFailureOrder)} | ConvertTo-Json -Depth 5 -Compress
            $sliceCases=@(
                @{name='ascending';left=0;right=2}, @{name='descending';left=2;right=0},
                @{name='negative';left=-1;right=-3}, @{name='missing';left=1;right=5},
                @{name='singleton';left=1;right=1}, @{name='null-left';left=$null;right=1})
            foreach($case in $sliceCases) {
                $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                try { Invoke-RangeSlice -Values @('a','b','c') -Left $case.left -Right $case.right -ErrorAction Continue -ErrorVariable faults 2>&1 |
                    ForEach-Object { $records.Add((Describe-Record $_)) } }
                catch { $records.Add((Describe-Record $_)) }
                [pscustomobject]@{kind='slice';case=$case.name;records=$records.ToArray();
                    faults=@($faults | ForEach-Object { Describe-Record $_ });errors=@($Error | ForEach-Object { Describe-Record $_ })} |
                    ConvertTo-Json -Depth 9 -Compress
            }
            $iteratorCases=@(
                @{name='ascending';left=1;right=3}, @{name='descending';left=3;right=1},
                @{name='single';left=1;right=1}, @{name='strings';left='1';right='3'},
                @{name='invalid-left';left='bad';right=2}, @{name='invalid-right';left=1;right='bad'})
            foreach ($case in $iteratorCases) {
                $Error.Clear(); $faults=@(); $records=[Collections.Generic.List[object]]::new()
                try { Invoke-RangeIterator -Left $case.left -Right $case.right -ErrorAction Continue -ErrorVariable faults 2>&1 |
                    ForEach-Object { $records.Add((Describe-Record $_)) } }
                catch { $records.Add((Describe-Record $_)) }
                [pscustomobject]@{kind='iterator';case=$case.name;records=$records.ToArray();
                    faults=@($faults | ForEach-Object { Describe-Record $_ });errors=@($Error | ForEach-Object { Describe-Record $_ })} |
                    ConvertTo-Json -Depth 9 -Compress
            }
            """;
        var original = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "native-match-range-original");
        var compiled = RunStatementErrorProbe(host,
            "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "native-match-range-compiled");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        var differences = expected.Select((line, index) => (line, actual: index < actual.Length ? actual[index] : "<missing>", index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(expected.Length == actual.Length && differences.Length == 0,
            string.Join(Environment.NewLine, differences.Take(4).Select(item =>
                $"Record {item.index}:{Environment.NewLine}Original: {item.line}{Environment.NewLine}Generated: {item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
