using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> InterpolationScalarHosts()
        => StatementErrorHosts().SelectMany(configuration => new[] { false, true }
            .Select(library => new object[] { configuration[0], configuration[1], library }));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(InterpolationScalarHosts))]
    public void Interpolation_PreservesScalarCultureAndPrecision(string framework, string host, bool library)
    {
        const string source = """
            function Expand-Scalar {
                [CmdletBinding()] param([double]$Number,[single]$Single,[decimal]$Decimal,[datetime]$Date)
                return "number=$Number;single=$Single;decimal=$Decimal;date=$Date"
            }
            """;
        const string probe = """
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=$culture
                foreach($number in 1.2345678901234567,1e-25,[double]::NaN,[double]::PositiveInfinity) {
                    Expand-Scalar -Number $number -Single ([single]1.23456789) -Decimal 1234.5678 -Date ([datetime]::new(2024,1,2,3,4,5))
                }
            }
            """;
        CompareInterpolationArtifact(framework, host, source, probe, 1, "scalar-interpolation", library);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Interpolation_PreservesOpenValuesSeparatorsAndFailureContinuation(string framework, string host)
    {
        const string source = """
            function Expand-Value { [CmdletBinding()] param([object]$Value) return "value=$Value" }
            function Expand-Recover { [CmdletBinding()] param([object]$Value) $text='before'; $text="value=$Value"; return $text }
            function Expand-WhatIf { [CmdletBinding(SupportsShouldProcess=$true)] param() return "whatif=$WhatIfPreference" }
            """;
        const string probe = """
            Add-Type 'public sealed class BrokenInterpolationValue { public override string ToString() { throw new System.InvalidOperationException("string failed"); } }'
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=$culture
                foreach($separator in ' ','::') {
                    $global:OFS=$separator
                    foreach($shape in 'Null','Boolean','Double','Array','NestedArray','EmptyArray','Map','Custom','CustomMethod','Wrapped','Broken') {
                        $value=switch($shape) {
                            'Null' { $null } 'Boolean' { $true } 'Double' { 1.2345678901234567 }
                            'Array' { ,([object[]]@(1,2)) } 'EmptyArray' { ,([object[]]@()) }
                            'NestedArray' { ,([object[]]@(1,([object[]]@(2,3)))) }
                            'Map' { @{one=1} } 'Custom' { [pscustomobject]@{one=1} }
                            'CustomMethod' { $item=[pscustomobject]@{one=1}; Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Value {'custom method'} -Force; $item }
                            'Wrapped' { $item=[psobject]1; Add-Member -InputObject $item -MemberType ScriptMethod -Name ToString -Value {'wrapped method'} -Force; $item }
                            'Broken' { [BrokenInterpolationValue]::new() }
                        }
                        foreach($name in 'Expand-Value','Expand-Recover') {
                            $global:Error.Clear()
                            $values=@(& $name -Value $value -ErrorAction Continue 2>&1)
                            $records=@(foreach($item in $values) {
                                if($item -is [Management.Automation.ErrorRecord]) {
                                    [pscustomobject]@{kind='error';type=$item.Exception.GetType().FullName;id=$item.FullyQualifiedErrorId.Split(',')[0]}
                                } else { [pscustomobject]@{kind='value';value=$item} }
                            })
                            [pscustomobject]@{culture=$culture;separator=$separator;shape=$shape;name=$name;records=$records;errors=$global:Error.Count} | ConvertTo-Json -Compress -Depth 5
                        }
                    }
                    foreach($preference in $null,$false,$true,'custom',([object[]]@(1,2))) {
                        $global:WhatIfPreference=$preference
                        Expand-WhatIf
                        Expand-WhatIf -WhatIf:$false
                        Expand-WhatIf -WhatIf
                    }
                }
            }
            """;
        CompareInterpolationArtifact(framework, host, source, probe, 3, "open-interpolation");
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void Interpolation_RuntimeFreeLibraryPreservesScalarWidthsAndNullables(string framework, string host)
    {
        var types = new[] { typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(bool), typeof(char), typeof(string),
            typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid), typeof(Version), typeof(Uri), typeof(DayOfWeek) };
        var source = string.Join(Environment.NewLine, types.Select(type =>
            "function Expand-" + type.Name + " { param([" + type.FullName + "]$Value); return \"value=$Value\" }")) +
            "\nfunction Expand-NullableDouble { param([Nullable[double]]$Value); return \"value=$Value\" }";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.ScalarInterpolationWidths", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(types.Length + 1, result.Manifest!.CompiledMethods);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        const string probe = """
            foreach($culture in 'en-US','pl-PL') {
                [Threading.Thread]::CurrentThread.CurrentCulture=$culture
                foreach($name in 'Byte','SByte','Int16','UInt16','Int32','UInt32','Int64','UInt64','Single','Double','Decimal','Boolean','Char','String',
                    'DateTime','DateTimeOffset','TimeSpan','Guid','Version','Uri','DayOfWeek','NullableDouble') {
                    $values=[Collections.Generic.List[object]]::new()
                    switch($name) {
                        'Boolean' { $values.Add($false); $values.Add($true) }
                        'Char' { $values.Add([char]'ę') }
                        'String' { $values.Add($null); $values.Add(''); $values.Add('żółć') }
                        'DateTime' { $values.Add([datetime]::new(2024,1,2,3,4,5)) }
                        'DateTimeOffset' { $values.Add([datetimeoffset]::new(2024,1,2,3,4,5,[timespan]::FromHours(2))) }
                        'TimeSpan' { $values.Add([timespan]::FromTicks(-123456789)) }
                        'Guid' { $values.Add([guid]'01234567-89ab-cdef-0123-456789abcdef') }
                        'Version' { $values.Add([version]'1.2.3.4') }
                        'Uri' { $values.Add([uri]'https://example.invalid/a%20b') }
                        'DayOfWeek' { $values.Add([DayOfWeek]::Wednesday) }
                        'NullableDouble' { $values.Add($null); $values.Add(1.2345678901234567) }
                        default {
                            $clrType=[type]('System.'+$name)
                            $values.Add([Activator]::CreateInstance($clrType))
                            $values.Add($clrType.GetField('MinValue').GetValue($null))
                            $values.Add($clrType.GetField('MaxValue').GetValue($null))
                            if($name -in 'Single','Double') {
                                foreach($field in 'NaN','NegativeInfinity','PositiveInfinity') { $values.Add($clrType.GetField($field).GetValue($null)) }
                            }
                        }
                    }
                    foreach($argument in $values) {
                        [pscustomobject]@{culture=$culture;name=$name;value=(Invoke-Case $name $argument)} | ConvertTo-Json -Compress
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($name,$argument) { & ('Expand-'+$name) $argument }; " + probe,
            fixture.RootPath, "original-scalar-interpolation-widths");
        var compiled = RunStatementErrorProbe(host, "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'); $type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Expand_Byte') }; " +
            "function Invoke-Case($name,$argument) { $type.GetMethod('Expand_'+$name).Invoke($null,[object[]]@($argument)) }; " + probe,
            fixture.RootPath, "compiled-scalar-interpolation-widths");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Empty(compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }

    private static void CompareInterpolationArtifact(string framework, string host, string source, string probe, int methods, string name, bool library = false)
    {
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.InterpolationValues", library ? PowerShellCompilationArtifactKind.Library : PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(methods, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        if (library)
        {
            Assert.False(result.Manifest.RequiresPowerShellRuntime);
            Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
            Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
        }
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-" + name);
        var compiledSetup = library
            ? "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) + "'); " +
              "$type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Expand_Scalar') }; " +
              "function Expand-Scalar { param([double]$Number,[single]$Single,[decimal]$Decimal,[datetime]$Date) " +
              "$type.GetMethod('Expand_Scalar').Invoke($null,[object[]]@($Number,$Single,$Decimal,$Date)) }; "
            : "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; ";
        var compiled = RunStatementErrorProbe(host, compiledSetup + probe,
            fixture.RootPath, "compiled-" + name);
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        Assert.Equal(original.StandardOutput, compiled.StandardOutput);
    }
}
