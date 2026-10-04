using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void OutputCapture_PreservesPartialErrorsAndAssignmentContinuation(string framework, string host)
    {
        var cases = new (string Name, string Body)[]
        {
            ("Body", "$Values=for ([int]$i=0;$i -lt 3;$i++) { if ($i -eq 1) { [int]::Parse($Text) }; $i }"),
            ("Condition", "$Values=for ([int]$i=0;$i -lt [int]::Parse($Text);$i++) { $i }"),
            ("Iterator", "$Values=for ([int]$i=0;$i -lt 3;$i += [int]::Parse($Text)) { $i }"),
            ("Initializer", "$Values=for ([int]$i=[int]::Parse($Text);$i -lt 3;$i++) { $i }"),
            ("While", "[int]$i=0; $Values=while ($i -lt [int]::Parse($Text)) { $i; $i++ }"),
            ("Do", "[int]$i=0; $Values=do { $i; $i++ } while ($i -lt [int]::Parse($Text))"),
            ("Until", "[int]$i=0; $Values=do { $i; $i++ } until ($i -ge [int]::Parse($Text))"),
            ("Foreach", "[byte]$i=[byte]0; $Values=foreach ($i in [Text.Encoding]::GetEncoding($Text).GetBytes('x')) { $i }"),
            ("Caught", "try { $Values=for ([int]$i=0;$i -lt 3;$i++) { $i; [int]::Parse($Text) } } catch { 'caught' }"),
            ("Nested", "$Values=for ([int]$i=0;$i -lt 2;$i++) { $Inner=for ([int]$j=0;$j -lt [int]::Parse($Text);$j++) { $j }; [void]$Trace.Add($Inner); $i }"),
            ("Finally", "$Values=for ([int]$i=0;$i -lt 2;$i++) { try { $i; [int]::Parse($Text) } finally { [void]$Trace.Add('cleanup') } }"),
        };
        var source = string.Join(Environment.NewLine, cases.Select(item =>
            "function Get-" + item.Name + "Capture { [CmdletBinding()] param([object]$Prior,[string]$Text,[Collections.ArrayList]$Trace) " +
            "$Values=$Prior; " + item.Body + "; [void]$Trace.Add($Values); 'after' }"));
        source += """

            function Get-FirstCapture {
                [CmdletBinding()] param([object]$Prior,[string]$Text,[Collections.ArrayList]$Trace)
                $Values=for ([int]$i=0;$i -lt [int]::Parse($Text);$i++) { $i }
                [void]$Trace.Add($Values)
                'after'
            }
            """;
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.OutputCaptureErrors", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            (result.Succeeded ? string.Empty : System.Text.Json.JsonSerializer.Serialize(
                new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                    new[] { fixture.ScriptPath }, "PowerForge.Compiled", "CaptureErrors", framework).Diagnostics)));
        Assert.Equal(12, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            function Describe-Record($record) {
                if ($record -is [Management.Automation.ErrorRecord]) {
                    return @{kind='error';id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;category=$record.CategoryInfo.Category.ToString()}
                }
                if ($record -is [Exception]) { return @{kind='exception';type=$record.GetType().FullName;message=$record.Message} }
                return @{kind='value';value=$record}
            }
            foreach ($name in 'Body','Condition','Iterator','Initializer','While','Do','Until','Foreach','Caught','Nested','Finally','First') {
                foreach ($seed in $false,$true) {
                    foreach ($text in 'invalid','2') {
                        foreach ($action in 'Continue','SilentlyContinue','Stop') {
                          foreach ($stop in $false,$true) {
                            $trace=[Collections.ArrayList]::new()
                            $records=[Collections.Generic.List[object]]::new()
                            $faults=@()
                            $inputText=if ($name -eq 'Foreach' -and $text -eq '2') {'utf-8'} else {$text}
                            $parameters=@{Prior=$(if ($seed) {'old'} else {$null});Text=$inputText;Trace=$trace;ErrorAction=$action;ErrorVariable='faults'}
                            if ($action -eq 'Stop') {
                                try {
                                    if ($stop) { & ('Get-'+$name+'Capture') @parameters 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                    else { & ('Get-'+$name+'Capture') @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                }
                                catch { [void]$records.Add((Describe-Record $_)) }
                            } elseif ($stop) { & ('Get-'+$name+'Capture') @parameters 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            else { & ('Get-'+$name+'Capture') @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            [pscustomobject]@{name=$name;seed=$seed;text=$text;action=$action;stop=$stop;records=$records.ToArray();
                                trace=$trace.ToArray();errors=@($faults | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 10 -Compress
                          }
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-capture-errors");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-capture-errors");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("FormatException", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("caught", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(288, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
