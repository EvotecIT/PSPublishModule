namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> NativePatternSourceHosts()
        => StatementErrorHosts().SelectMany(host => new[] { "\n", "\r\n" }
            .Select(lineEnding => new[] { host[0], host[1], lineEnding }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(NativePatternSourceHosts))]
    public void NativeReplace_PreservesCollectionsCallbacksErrorsAndAssignment(string framework, string host, string lineEnding)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-ReplaceFlow {
                [CmdletBinding()] param([string]$Mode, [object]$Value, [object]$Pattern)
                'before'
                $result='unchanged'
                if ($Mode -eq 'replace') { $result=$Value -replace $Pattern }
                if ($Mode -eq 'ireplace') { $result=$Value -ireplace $Pattern }
                if ($Mode -eq 'creplace') { $result=$Value -creplace $Pattern }
                'after'
                $result
            }
            function Invoke-BasicReplace { param($Value,$Pattern) $Value -replace $Pattern; 'basic-after' }
            """.ReplaceLineEndings(lineEnding), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeReplace", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        foreach (var name in new[] { "Invoke-ReplaceFlow", "Invoke-BasicReplace" })
        {
            var unit = Assert.Single(result.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        }
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Collections.Generic;
            public sealed class ReplaceValue {
                public static readonly List<string> Events = new List<string>();
                public string Name; public bool Fail;
                public override string ToString() { Events.Add("text:"+Name); if(Fail) throw new InvalidOperationException("conversion-fault"); return Name; }
            }
            public sealed class ReplaceSequence : IEnumerable, IEnumerator, IDisposable {
                public string Failure; private int index=-1;
                public IEnumerator GetEnumerator() { ReplaceValue.Events.Add("get"); if(Failure=="get") throw new InvalidOperationException("get-fault"); return this; }
                public bool MoveNext() { index++; ReplaceValue.Events.Add("move:"+index); if(index==1 && Failure=="move") throw new InvalidOperationException("move-fault"); return index<2; }
                public object Current { get { ReplaceValue.Events.Add("current:"+index); if(index==1 && Failure=="current") throw new InvalidOperationException("current-fault"); return index==0?"Alpha":"alpha"; } }
                public void Reset() { throw new NotSupportedException(); }
                public void Dispose() { ReplaceValue.Events.Add("dispose"); if(Failure=="dispose") throw new InvalidOperationException("dispose-fault"); }
            }
            '@
            function Describe-ReplaceRecord($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{error=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        category=[string]$item.CategoryInfo.Category;message=$item.Exception.Message;
                        line=$item.InvocationInfo.ScriptLineNumber;column=$item.InvocationInfo.OffsetInLine;source=$item.InvocationInfo.Line}
                } elseif ($null -eq $item) { 'null' }
                else { [pscustomobject]@{type=$item.GetType().FullName;value=$item} }
            }
            $factories=@(
                { @{value='Alpha alpha';pattern=@('a','x')} },
                { @{value=@('Alpha','alpha','beta');pattern=@('a','x')} },
                { @{value=$null;pattern=@('a','x')} },
                { @{value=@();pattern=@('a','x')} },
                { @{value=@(@('Alpha','alpha'),$null,42);pattern=@('a','x')} },
                { @{value='123';pattern='\d'} },
                { @{value='abc';pattern=@('[','x')} },
                { @{value='abc';pattern=@('a','x','extra')} },
                { @{value='abc';pattern=@()} },
                { @{value='abc';pattern=$null} },
                { @{value='a12b34';pattern=@('(\d+)','<$1>')} },
                { @{value=1.25;pattern=@(',','.')} },
                { @{value=[ReplaceValue]@{Name='Alpha'};pattern=@([ReplaceValue]@{Name='a'},[ReplaceValue]@{Name='x'})} },
                { @{value=[ReplaceValue]@{Name='Alpha';Fail=$true};pattern=@('a','x')} },
                { @{value='Alpha';pattern=@([ReplaceValue]@{Name='a';Fail=$true},'x')} },
                { @{value='Alpha';pattern=@('a',[ReplaceValue]@{Name='x';Fail=$true})} },
                { @{value=[ReplaceSequence]@{Failure=''};pattern=@('a','x')} },
                { @{value=[ReplaceSequence]@{Failure='get'};pattern=@('a','x')} },
                { @{value=[ReplaceSequence]@{Failure='move'};pattern=@('a','x')} },
                { @{value=[ReplaceSequence]@{Failure='current'};pattern=@('a','x')} },
                { @{value=[ReplaceSequence]@{Failure='dispose'};pattern=@('a','x')} },
                { @{value='a1b2';pattern=@('\d',{ [ReplaceValue]::Events.Add('callback:'+$_); $_.Value+'!' })} },
                { @{value='a1b2';pattern=@('\d',{ [ReplaceValue]::Events.Add('callback:'+$_); if($_.Value -eq '2'){throw 'callback-fault'}; 'x' })} }
            )
            foreach ($culture in 'en-US','de-DE') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach ($mode in 'replace','ireplace','creplace') {
                    for ($index=0; $index -lt $factories.Count; $index++) {
                        foreach ($action in 'Continue','SilentlyContinue','Stop') {
                            $case=& $factories[$index]; [ReplaceValue]::Events.Clear(); $Error.Clear(); $faults=@()
                            $Matches=@{sentinel='kept'}; $records=[Collections.Generic.List[object]]::new()
                            try { Invoke-ReplaceFlow -Mode $mode -Value $case.value -Pattern $case.pattern -ErrorAction $action -ErrorVariable faults 2>&1 |
                                ForEach-Object { $records.Add((Describe-ReplaceRecord $_)) } }
                            catch { $records.Add((Describe-ReplaceRecord $_)) }
                            [pscustomobject]@{culture=$culture;mode=$mode;case=$index;action=$action;records=$records.ToArray();
                                events=[ReplaceValue]::Events.ToArray();matches=$Matches;
                                faults=@($faults | ForEach-Object { Describe-ReplaceRecord $_ });
                                errors=@($Error | ForEach-Object { Describe-ReplaceRecord $_ })} | ConvertTo-Json -Depth 9 -Compress
                        }
                    }
                }
            }
            @(Invoke-BasicReplace -Value 'Alpha' -Pattern @('a','x') -Verbose) -join '|'
            @(Invoke-ReplaceFlow -Mode replace -Value 'Alpha' -Pattern @('a','x') | Select-Object -First 1) -join '|'
            @(Invoke-ReplaceFlow -Mode replace -Value 'Alpha' -Pattern @('a','x')) -join '|'
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "replace");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "replace");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.Length >= 417);
        var differences = expected.Select((line, index) => (line, actual: actual[index], index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.Take(5)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
