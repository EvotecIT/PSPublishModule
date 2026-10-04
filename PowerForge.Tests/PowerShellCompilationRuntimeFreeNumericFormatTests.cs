using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void RuntimeFreeFormatting_QualifiesAllAdmittedNumericWidths(string framework, string host)
    {
        var types = new[] { typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal) };
        var source = string.Join(Environment.NewLine, types.Select(type =>
            "function Format-" + type.Name + " { param([" + type.FullName + "]$Value); return '{0:C2}|{0:E2}|{0:F2}|{0:G2}|{0:N2}|{0:P0}' -f $Value }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NumericFormatWidths", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(11, result.Manifest!.CompiledMethods);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
        const string probe = """
            foreach($culture in 'en-US','pl-PL','de-DE') {
                [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($name in 'Byte','SByte','Int16','UInt16','Int32','UInt32','Int64','UInt64','Single','Double','Decimal') {
                    $clrType=[type]('System.'+$name)
                    $values=[Collections.Generic.List[object]]::new()
                    [void]$values.Add([Activator]::CreateInstance($clrType))
                    [void]$values.Add($clrType.GetField('MinValue').GetValue($null))
                    [void]$values.Add($clrType.GetField('MaxValue').GetValue($null))
                    if($name -in 'Single','Double') {
                        foreach($field in 'NaN','NegativeInfinity','PositiveInfinity') { [void]$values.Add($clrType.GetField($field).GetValue($null)) }
                    }
                    foreach($number in $values) {
                        $value=Invoke-Case $name $number
                        [pscustomobject]@{culture=$culture;name=$name;argumentType=$number.GetType().FullName;type=$value.GetType().FullName;value=$value} | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($name,$number) { & ('Format-'+$name) $number }; " + probe,
            fixture.RootPath, "original-runtimefree-format-widths");
        var compiled = RunStatementErrorProbe(host, "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'); $type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Format_Byte') }; " +
            "function Invoke-Case($name,$number) { $type.GetMethod('Format_'+$name).Invoke($null,[object[]]@($number)) }; " + probe,
            fixture.RootPath, "compiled-runtimefree-format-widths");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Empty(compiled.StandardError);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(117, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
    }
}
