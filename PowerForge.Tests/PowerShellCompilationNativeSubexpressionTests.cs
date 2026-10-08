namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeSubexpressions_PreserveValueCardinalityEffectsAndContinuation(string framework, string host)
    {
        var expressions = new[] {
            "$($n=$Value)", "$($n=$Value; $n)", "@($n=$Value)", "@($n=$Value; $n)",
            "$($n=[int]'invalid'; 'tail')", "@($n=[int]'invalid'; 'tail')",
            "$()", "$($null)", "$($Value)", "$(,$Value)", "$($Value; 'tail')", "$(@($Value))",
            "$($($Value))", "$($n++)", "$(($n++))", "$($n++; 'tail')", "$([void]1; $Value)",
            "$(Write-Output -NoEnumerate $Value)", "$('first'; Write-Error 'capture-fault'; 'last')",
            "$('first'; [int]'invalid'; 'last')", "$(Write-Output 'first'; Write-Output $Value; 'last')",
            "$($Value; $?)", "$($MyInvocation.MyCommand.Name)", "$($Value; ,$Value)",
            "$([void]$null)", "$([void]($n++); $n)", "@([void]$null)", "@([void]$Value; 'tail')", "$((Write-Output -NoEnumerate $Value))", "@(Write-Output -NoEnumerate $Value)", "@('head'; (Write-Output -NoEnumerate $Value); 'tail')"
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, expressions.Select((expression, index) =>
            "function Read-Sub" + index + " { [CmdletBinding()] param([object]$Value) " +
            "'before'; $n=2; $result='old'; $result=" + expression + "; 'after'; ,$result; \"n=$n\"; " +
            "[object]::ReferenceEquals($result,$Value) }")), ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeSubexpression", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var units = result.Manifest!.UnitDispositionLedger!.Entries.Where(unit => unit.Name.StartsWith("Read-Sub", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expressions.Length, units.Length);
        Assert.All(units, unit => {
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
            Assert.True(unit.UsesNativeFunctionBinding);
            Assert.False(unit.RetainedHostedSource);
        });
        var probe = """
            Add-Type -TypeDefinition @'
            using System;
            using System.Collections;
            using System.Collections.Generic;
            public sealed class SubSequence : IEnumerable, IEnumerator {
                public static readonly List<string> Events=new List<string>();
                public string Failure; private int index=-1;
                public IEnumerator GetEnumerator() { Events.Add("get"); if(Failure=="get")throw new InvalidOperationException("get-fault"); return this; }
                public bool MoveNext(){index++;Events.Add("move:"+index);if(index==1 && Failure=="move")throw new InvalidOperationException("move-fault");return index<2;}
                public object Current {get{Events.Add("current:"+index);if(index==1 && Failure=="current")throw new InvalidOperationException("current-fault");return index;}}
                public void Reset(){throw new NotSupportedException();}
            }
            '@
            function Describe-SubValue($item) {
                if ($null -eq $item) { return [pscustomobject]@{kind='null'} }
                if ($item -is [Management.Automation.ErrorRecord]) {
                    return [pscustomobject]@{kind='error';id=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;
                        message=$item.Exception.Message;category=[string]$item.CategoryInfo.Category}
                }
                if($item -is [Exception]) { return [pscustomobject]@{kind='exception';type=$item.GetType().FullName;message=$item.Message} }
                if($item -is [Array]) {return [pscustomobject]@{kind=$item.GetType().FullName;items=@(foreach($v in $item){Describe-SubValue $v})}}
                if($item -is [SubSequence]) {return [pscustomobject]@{kind='SubSequence'}}
                [pscustomobject]@{kind=$item.GetType().FullName;value=$item}
            }
            $factories=@({$null},{,(@())},{42},{'text'},{,(1,2)},{,(@((1,2),(3,4)))},
                {,[SubSequence]@{Failure=''}},{,[SubSequence]@{Failure='get'}},
                {,[SubSequence]@{Failure='move'}},{,[SubSequence]@{Failure='current'}})
            for($function=0;$function -lt FUNCTION_COUNT;$function++) {
                for($case=0;$case -lt $factories.Count;$case++) {
                    foreach($action in 'Continue','SilentlyContinue','Stop') {
                        $value=& $factories[$case]; [SubSequence]::Events.Clear();$Error.Clear();$faults=@();$records=[Collections.Generic.List[object]]::new()
                        try {& ('Read-Sub'+$function) -Value $value -ErrorAction $action -ErrorVariable faults 2>&1 |
                            ForEach-Object {$records.Add((Describe-SubValue $_))}}
                        catch {$records.Add((Describe-SubValue $_))}
                        [pscustomobject]@{function=$function;case=$case;action=$action;records=$records.ToArray();events=[SubSequence]::Events.ToArray();
                            faults=@($faults|ForEach-Object {Describe-SubValue $_});errors=@($Error|ForEach-Object {Describe-SubValue $_})} | ConvertTo-Json -Depth 12 -Compress
                    }
                }
            }
            @(Read-Sub10 -Value (1,2) | Select-Object -First 2) | ConvertTo-Json -Compress
            @(Read-Sub10 -Value 'later') | ConvertTo-Json -Depth 5 -Compress
            """.Replace("FUNCTION_COUNT", expressions.Length.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "subexpression");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "subexpression");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.Length >= expressions.Length * 30 + 2);
        var differences = expected.Select((line, index) => (line, actual: actual[index], index))
            .Where(item => item.line != item.actual).ToArray();
        Assert.True(differences.Length == 0, string.Join(Environment.NewLine, differences.GroupBy(item => item.index / 30).Select(group => group.First()).Take(12)
            .Select(item => $"Record {item.index}: original={item.line}{Environment.NewLine}generated={item.actual}")));
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
