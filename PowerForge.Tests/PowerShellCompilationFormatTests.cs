using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void ScalarFormatting_RetainsRuntimeIndependentDynamicTemplates()
    {
        using var fixture = ArtifactFixture.Create("function Format-Value { param([string]$Template,[double]$Value); return $Template -f $Value }");
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("separately proven safe template", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("object")]
    [InlineData("object[]")]
    [InlineData("int[]")]
    public void ScalarFormatting_RetainsUnqualifiedArgumentShapes(string type)
    {
        using var fixture = ArtifactFixture.Create("function Format-Value { [CmdletBinding()] param([string]$Template,[" + type + "]$Value); return $Template -f $Value }", ".psm1");
        var result = new PowerShellTypedCompilationTranspiler().TranspileForBinaryModule(
            new[] { fixture.ScriptPath }, "PowerForge.Compiled", "FormatShapes", "net10.0");
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("qualified scalar argument", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void ScalarFormatting_PreservesCultureErrorsAndContinuation(string framework, string host)
    {
        var source = """
            function Format-Number { [CmdletBinding()] param([string]$Template,[double]$Value); $Template -f $Value; 'after' }
            function Format-Union { [CmdletBinding()] param([string]$Template,[double]$Value); $Number=0; $Number+=$Value; $Template -f $Number; 'after' }
            function Format-Integer { [CmdletBinding()] param([string]$Template,[int]$Value); $Template -f $Value; 'after' }
            function Format-Text { [CmdletBinding()] param([string]$Template,[string]$Value); $Template -f $Value; 'after' }
            function Format-Caught { [CmdletBinding()] param([string]$Template,[double]$Value); try { $Template -f $Value } catch [Management.Automation.RuntimeException] { 'caught' } finally { 'finally' }; 'after' }
            function Format-Order { [CmdletBinding()] param([string]$Template,[Collections.ArrayList]$Trace); $Template.Substring($Trace.Add('left')) -f $Trace.Add('right'); 'after' }
            function Format-Assigned { [CmdletBinding()] param([string]$Template,[double]$Value); $Result='before'; $Result=$Template -f $Value; $Result; 'after' }
            function Format-Leaf { [CmdletBinding()] param([string]$Template,[double]$Value); return $Template -f $Value }
            function Format-Caller { [CmdletBinding()] param([string]$Template,[double]$Value); Format-Leaf -Template $Template -Value $Value; 'after' }
            """;
        var scalarTypes = new[] { "byte", "sbyte", "short", "ushort", "uint", "long", "ulong", "single", "decimal", "bool", "char", "DateTime", "TimeSpan", "Guid" };
        var clrTypes = new[] { typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(decimal), typeof(bool), typeof(char), typeof(DateTime), typeof(TimeSpan), typeof(Guid) };
        source += Environment.NewLine + string.Join(Environment.NewLine, scalarTypes.Select((type, index) =>
            "function Format-" + type + " { [CmdletBinding()] param([string]$Template,[" + clrTypes[index].FullName + "]$Value); $Template -f $Value; 'after' }"));
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScalarFormatting", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(23, result.Manifest!.CompiledMethods);
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
            foreach ($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach ($name in 'Number','Union','Integer','Text','Caught','Order','Assigned','Leaf','Caller',
                    'byte','sbyte','short','ushort','uint','long','ulong','single','decimal','bool','char','DateTime','TimeSpan','Guid') {
                    foreach ($template in '{0:N2}','<{0,12:N0}>','{{{0}}}','{1}','{','{0:Q}','plain','',$null) {
                        foreach ($action in 'Continue','SilentlyContinue','Stop') {
                            $trace=[Collections.ArrayList]::new()
                            $records=[Collections.Generic.List[object]]::new()
                            $faults=@()
                            $parameters=@{Template=$template;ErrorAction=$action;ErrorVariable='faults'}
                            if ($name -eq 'Order') {$parameters.Trace=$trace} else {
                                $parameters.Value=switch ($name) {
                                    'byte' {255} 'sbyte' {-128} 'short' {-32768} 'ushort' {65535}
                                    'uint' {[System.UInt32]::MaxValue} 'long' {[long]::MinValue} 'ulong' {[System.UInt64]::MaxValue}
                                    'single' {[single]::NaN} 'decimal' {[decimal]::MaxValue} 'bool' {$true} 'char' {[char]'x'}
                                    'DateTime' {[datetime]::new(2026,9,7,12,34,56)} 'TimeSpan' {[timespan]::FromTicks(-123456789)}
                                    'Guid' {[guid]'01234567-89ab-cdef-0123-456789abcdef'}
                                    'Text' {if ($null -eq $template) {$null} else {1234.5}} default {1234.5}
                                }
                            }
                            if ($action -eq 'Stop') {
                                try { & ('Format-'+$name) @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                catch { [void]$records.Add((Describe-Record $_)) }
                            } else { & ('Format-'+$name) @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            [pscustomobject]@{culture=$culture;name=$name;template=$template;action=$action;records=$records.ToArray();trace=$trace.ToArray();
                                errors=@($faults | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 10 -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-scalar-format");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-scalar-format");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Contains("FormatError", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("caught", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1242, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
