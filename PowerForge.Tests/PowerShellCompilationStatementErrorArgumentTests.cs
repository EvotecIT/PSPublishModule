namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StatementErrorEmission_EvaluatesArgumentsBeforeEnteringLocalCommand(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-ArgumentInner {
                [CmdletBinding()] param([int]$First, [int]$Second, [string]$Text)
                'inner-entered'
                $First
                $Second
                [void][int]::Parse($Text)
                'inner-after'
            }
            function Read-DeclaredArguments {
                [CmdletBinding()] param([string]$First, [string]$Second, [string]$Text)
                'outer-before'
                Read-ArgumentInner -First ([int]::Parse($First)) -Second ([int]::Parse($Second)) -Text $Text
                'outer-after'
            }
            function Read-ReversedArguments {
                [CmdletBinding()] param([string]$First, [string]$Second, [string]$Text)
                'outer-before'
                Read-ArgumentInner -Second ([int]::Parse($Second)) -First ([int]::Parse($First)) -Text $Text
                'outer-after'
            }
            function Read-PositionalArguments {
                [CmdletBinding()] param([string]$First, [string]$Second, [string]$Text)
                'outer-before'
                Read-ArgumentInner ([int]::Parse($First)) ([int]::Parse($Second)) $Text
                'outer-after'
            }
            function Read-ObservedArguments {
                [CmdletBinding()] param([Text.StringBuilder]$Trace, [string]$Text)
                Read-ArgumentInner -First ($Trace.Append('F').Length) -Second ([int]::Parse($Text)) -Text '3'
                'outer-after'
            }
            function Read-ReversedObservedArguments {
                [CmdletBinding()] param([Text.StringBuilder]$Trace, [string]$Text)
                Read-ArgumentInner -Second ([int]::Parse($Text)) -First ($Trace.Append('F').Length) -Text '3'
                'outer-after'
            }
            """, ".psm1");
        var assembly = BuildStatementErrorEmission(fixture, framework, 6);
        const string probe = """
            function Describe-Fault($item) {
                $record = if ($item -is [Management.Automation.ErrorRecord]) { $item } else { $item.ErrorRecord }
                $item.GetType().Name + ':' + $record.FullyQualifiedErrorId + ':' + $record.Exception.GetType().FullName + ':' + $record.Exception.Message
            }
            foreach ($command in 'Read-DeclaredArguments','Read-ReversedArguments','Read-PositionalArguments') {
                foreach ($action in 'Continue','SilentlyContinue','Ignore','Stop') {
                    foreach ($values in @(@('1','2','3'),@('bad-first','2','3'),@('1','bad-second','3'),@('bad-first','bad-second','3'),@('1','2','bad-inner'))) {
                        foreach ($callerCatch in $false,$true) {
                            if (!$callerCatch -and $action -eq 'Stop') { continue }
                            $faults=@(); $Error.Clear()
                            $records=[Collections.Generic.List[string]]::new()
                            if ($callerCatch) {
                                try {
                                    & $command -First $values[0] -Second $values[1] -Text $values[2] -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                        if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add((Describe-Fault $_)) }
                                        else { [void]$records.Add([string]$_) }
                                    }
                                } catch { [void]$records.Add('caller-catch:' + (Describe-Fault $_)) }
                            } else {
                                & $command -First $values[0] -Second $values[1] -Text $values[2] -ErrorAction $action -ErrorVariable faults 2>&1 | ForEach-Object {
                                    if ($_ -is [Management.Automation.ErrorRecord]) { [void]$records.Add((Describe-Fault $_)) }
                                    else { [void]$records.Add([string]$_) }
                                }
                            }
                            [pscustomobject]@{ command=$command; action=$action; values=$values; callerCatch=$callerCatch; records=$records.ToArray();
                                faults=@($faults | ForEach-Object { Describe-Fault $_ }); errors=@($Error | ForEach-Object { Describe-Fault $_ }) } | ConvertTo-Json -Compress
                        }
                    }
                }
            }
            foreach ($command in 'Read-ObservedArguments','Read-ReversedObservedArguments') {
                foreach ($text in '2','bad') {
                    $trace = [Text.StringBuilder]::new()
                    $records = @(& $command -Trace $trace -Text $text -ErrorAction Continue 2>&1 | ForEach-Object {
                        if ($_ -is [Management.Automation.ErrorRecord]) { Describe-Fault $_ } else { [string]$_ }
                    })
                    [pscustomobject]@{ command=$command; text=$text; trace=$trace.ToString(); records=$records } | ConvertTo-Json -Compress
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-argument-emission");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(assembly) + "'; " + probe,
            fixture.RootPath, "compiled-argument-emission");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Contains("inner-entered", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("FormatException,Read-ArgumentInner", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
