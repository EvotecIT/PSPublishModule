using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void StringParameterBinding_PreservesScriptConversionAndValidation(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Get-RequiredText { [CmdletBinding()] param([Parameter(Mandatory)][string]$Text); return '['+$Text+']' }
            function Get-NullableText { [CmdletBinding()] param([Parameter(Mandatory)][AllowNull()][string]$Text); return '['+$Text+']' }
            function Get-EmptyText { [CmdletBinding()] param([Parameter(Mandatory)][AllowEmptyString()][string]$Text); return '['+$Text+']' }
            function Get-OptionalText { [CmdletBinding()] param([string]$Text='seed'); return '['+$Text+']' }
            function Get-BasicText { param([string]$Text); return '['+$Text+']' }
            function Get-ValidatedText { [CmdletBinding()] param([ValidateNotNullOrEmpty()][string]$Text); return '['+$Text+']' }
            function Get-PipelineText { [CmdletBinding()] param([Parameter(Mandatory,ValueFromPipeline)][ValidateNotNullOrEmpty()][string]$Text); return '['+$Text+']' }
            function Get-PropertyText { [CmdletBinding()] param([Parameter(Mandatory,ValueFromPipelineByPropertyName)][ValidateNotNullOrEmpty()][string]$Text); return '['+$Text+']' }
            function Get-PipelineArray { [CmdletBinding()] param([Parameter(Mandatory,ValueFromPipeline)][ValidateNotNullOrEmpty()][int[]]$Values); return 'array-after' }
            function Get-PipelineObject { [CmdletBinding()] param([Parameter(Mandatory,ValueFromPipeline)][ValidateNotNullOrEmpty()][object]$Value); return 'object-after' }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.StringParameters", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(10, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.PromotedTypedRegions);
        Assert.Equal(5, result.Manifest.RuntimeFallbackUnits); // Native parameter binding remains runtime-owned.
        Assert.Equal(5, result.Manifest.UnitDispositionLedger!.Entries.Count(unit => unit.UsesNativeFunctionBinding));
        Assert.DoesNotContain(result.Manifest.UnitDispositionLedger.Entries, unit => unit.RetainedHostedSource);
        const string probe = """
            Add-Type -TypeDefinition @'
            using System;
            public sealed class StringInputProbe {
                public static int Calls;
                public bool Fail;
                public override string ToString() {
                    Calls++;
                    if (Fail) throw new InvalidOperationException("string conversion failed");
                    return "converted";
                }
            }
            '@
            function Describe-StringBindingError($item) {
                if ($item -is [Management.Automation.ErrorRecord]) {
                    [pscustomobject]@{id=$item.FullyQualifiedErrorId;type=$item.Exception.GetType().FullName;message=$item.Exception.Message;category=[string]$item.CategoryInfo.Category}
                } else { [pscustomobject]@{type=$item.GetType().FullName;message=$item.Message} }
            }
            $referenceValue='reference'
            $throwing=[StringInputProbe]::new(); $throwing.Fail=$true
            $cases=@(
                @{name='null';value=$null}, @{name='automation-null';value=[Management.Automation.Internal.AutomationNull]::Value},
                @{name='empty';value=''}, @{name='text';value='abc'}, @{name='number';value=12.5},
                @{name='date';value=[datetime]::new(2020,2,3)}, @{name='reference';value=[ref]$referenceValue},
                @{name='array-empty';value=@()}, @{name='array-one';value=@('abc')}, @{name='array-many';value=@('a','b')},
                @{name='char-array';value=[char[]]'ab'}, @{name='list';value=[Collections.ArrayList]@('a','b')},
                @{name='wrapped';value=[psobject]12.5}, @{name='custom';value=[StringInputProbe]::new()}, @{name='throwing';value=$throwing})
            foreach ($culture in 'en-US','pl-PL') {
                [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach ($command in 'Get-RequiredText','Get-NullableText','Get-EmptyText','Get-OptionalText','Get-BasicText','Get-ValidatedText') {
                    foreach ($case in $cases) {
                        $records=@(); $faults=@(); $caught=@(); $Error.Clear(); [StringInputProbe]::Calls=0
                        try { $records=@(& $command -Text $case.value -ErrorAction Stop -ErrorVariable faults) }
                        catch { $caught=@(Describe-StringBindingError $_) }
                        [pscustomobject]@{culture=$culture;command=$command;case=$case.name;records=$records;calls=[StringInputProbe]::Calls;caught=$caught;
                            faults=@($faults | ForEach-Object { Describe-StringBindingError $_ });
                            errors=@($Error | ForEach-Object { Describe-StringBindingError $_ })} | ConvertTo-Json -Depth 7 -Compress
                    }
                }
                foreach ($command in 'Get-PipelineText','Get-PropertyText','Get-PipelineArray','Get-PipelineObject') {
                    foreach ($inputCase in 'empty','null','one','invalid-middle') {
                        $values=switch($inputCase) { empty { ,@() }; null { ,@($null) }; one { ,@('12') }; default { ,@('12',$null,'34') } }
                        if ($command -eq 'Get-PropertyText') { $values=@($values | ForEach-Object { [pscustomobject]@{Text=$_} }) }
                        $records=@(); $faults=@(); $caught=@(); $Error.Clear()
                        try { $records=@($values | & $command -ErrorAction Stop -ErrorVariable faults) }
                        catch { $caught=@(Describe-StringBindingError $_) }
                        [pscustomobject]@{culture=$culture;command=$command;case=$inputCase;records=$records;caught=$caught;
                            faults=@($faults | ForEach-Object { Describe-StringBindingError $_ });
                            errors=@($Error | ForEach-Object { Describe-StringBindingError $_ })} | ConvertTo-Json -Depth 7 -Compress
                    }
                }
                'omitted:'+(Get-OptionalText)
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "string-parameters");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "string-parameters");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
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
