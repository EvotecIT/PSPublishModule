using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("[Math]::Abs($Index)")]
    [InlineData("[Convert]::ToString($Index)")]
    [InlineData("[Collections.ArrayList]::new($Index)")]
    [InlineData("[string]::Concat($Index,$Index,$Index,$Index,$Index)")]
    public void NumericArguments_RetainUnqualifiedOverloadDispatch(string call)
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { [CmdletBinding()] param([double]$Delta); $Index=0; $Index+=$Delta; " + call + " }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericOverloads", "net10.0");
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("overload dispatch", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void NumericArguments_DoNotIntroduceAHostIntoRuntimeIndependentLibraries()
    {
        using var fixture = ArtifactFixture.Create(
            "function Get-Value { param([double]$Delta); $Index=0; $Index+=$Delta; return 'abc'.Substring($Index,1) }");
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("No exact CLR overload", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NumericArguments_PreserveConversionErrorsAndEvaluationOrder(string framework, string host)
    {
        var cases = new (string Name, string Body)[]
        {
            ("Substring", "'abcdef'.Substring($Index,1)"),
            ("Static", "[char]::ConvertFromUtf32($Index)"),
            ("Constructor", "$Random=[Random]::new($Index); $Random.Next()"),
            ("Receiver", "$Items.RemoveAt($Index)"),
            ("Order", "'abcdef'.Substring($Index,$Trace.Add('arg2'))"),
            ("ReceiverOrder", "$Items.RemoveRange($Index,$Trace.Add('arg2'))"),
            ("Multiple", "$Length=0; $Length+=$Count; 'abcdef'.Substring($Index,$Length)"),
            ("ArgumentFailure", "'abcdef'.Substring($Index,[int]::Parse($Text))"),
            ("Boxed", "return [object]::ReferenceEquals($Index,$Index)"),
        };
        var source = string.Join(Environment.NewLine, cases.Select(item =>
            "function Get-" + item.Name + "Numeric { [CmdletBinding()] param([double]$Delta,[bool]$Integral,[double]$Count,[string]$Text,[Collections.ArrayList]$Items,[Collections.ArrayList]$Trace) " +
            "$Index=0; if ($Integral) { $Index+=1 } else { $Index+=$Delta }; " + item.Body +
            (item.Body.StartsWith("return ", StringComparison.Ordinal) ? " }" : "; 'after' }")));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericArguments", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput + Environment.NewLine +
            (result.Succeeded ? string.Empty : System.Text.Json.JsonSerializer.Serialize(
                new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
                    new[] { fixture.ScriptPath }, "PowerForge.Compiled", "NumericArguments", framework).Diagnostics)));
        Assert.Equal(9, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            function Describe-Record($record) {
                if ($record -is [Management.Automation.ErrorRecord]) {
                    return @{kind='error';id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;
                        category=$record.CategoryInfo.Category.ToString();message=$record.Exception.Message}
                }
                if ($record -is [Exception]) { return @{kind='exception';type=$record.GetType().FullName;message=$record.Message} }
                return @{kind='value';value=$record;type=$(if ($null -eq $record) {'null'} else {$record.GetType().FullName})}
            }
            $inputs=@(
                @{label='integer';delta=0.0;integral=$true},
                @{label='zero';delta=0.0;integral=$false},
                @{label='half';delta=0.5;integral=$false},
                @{label='one-half';delta=1.5;integral=$false},
                @{label='two-half';delta=2.5;integral=$false},
                @{label='negative';delta=-1.5;integral=$false},
                @{label='int-boundary';delta=2147483647.5;integral=$false},
                @{label='nan';delta=[double]::NaN;integral=$false},
                @{label='infinity';delta=[double]::PositiveInfinity;integral=$false},
                @{label='huge';delta=1e100;integral=$false})
            foreach ($name in 'Substring','Static','Constructor','Receiver','Order','ReceiverOrder','Multiple','ArgumentFailure','Boxed') {
                foreach ($inputCase in $inputs) {
                    $variants=if ($name -in 'Receiver','ReceiverOrder','Multiple','ArgumentFailure') {0,1} else {0}
                    foreach ($variant in $variants) {
                        foreach ($action in 'Continue','SilentlyContinue','Stop') {
                            $trace=[Collections.ArrayList]::new()
                            $items=if ($name -in 'Receiver','ReceiverOrder' -and $variant -eq 1) {$null} else {,[Collections.ArrayList]@(0,1,2,3,4)}
                            $records=[Collections.Generic.List[object]]::new()
                            $faults=@()
                            $parameters=@{Delta=$inputCase.delta;Integral=$inputCase.integral;Count=$(if ($name -eq 'Multiple' -and $variant -eq 1) {1e100} else {1.5});
                                Text=$(if ($name -eq 'ArgumentFailure' -and $variant -eq 1) {'invalid'} else {'1'});
                                Items=$items;Trace=$trace;ErrorAction=$action;ErrorVariable='faults'}
                            if ($action -eq 'Stop') {
                                try { & ('Get-'+$name+'Numeric') @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                catch { [void]$records.Add((Describe-Record $_)) }
                            } else { & ('Get-'+$name+'Numeric') @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            [pscustomobject]@{name=$name;input=$inputCase.label;variant=$variant;action=$action;records=$records.ToArray();trace=$trace.ToArray();
                                items=$(if ($null -ne $items) {$items.ToArray()} else {$null});errors=@($faults | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 10 -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-numeric-arguments");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-numeric-arguments");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Contains("MethodArgumentConversionInvalidCastArgument", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("InvokeMethodOnNull", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("arg2", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(390, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
