using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(true)]
    [InlineData(false)]
    public void RuntimeFreeFormatting_AccountsForProcessBackedges(bool replacesLabel)
    {
        var source = "function Format-Records { [CmdletBinding()] [OutputType([string])] param([Parameter(ValueFromPipeline)][string]$Value) " +
            "begin {$Label='ms'} process { \"{0:N2}$Label\" -f 1.0; " + (replacesLabel ? "$Label=$Value" : string.Empty) + " } end {'done'} }";
        using var fixture = ArtifactFixture.Create(source);
        var result = new PowerShellSemanticCompilationPipeline().Compile(
            new[] { PowerShellSourceParser.Parse(source, fixture.ScriptPath) }, "net10.0", PowerShellCompilationCapabilities.TypedExecutable);
        if (replacesLabel)
        {
            Assert.Empty(result.Emitted.Methods);
            Assert.Contains(result.Emitted.Diagnostics, diagnostic => diagnostic.Message.Contains("separately proven safe template", StringComparison.Ordinal));
        }
        else Assert.Single(result.Emitted.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("$Label=$Text; return \"{0:N2}$Label\" -f $Value")]
    [InlineData("if($Compact){$Label='ms'}else{$Label='{1}'}; return \"{0:N2}$Label\" -f $Value")]
    [InlineData("if($Compact){$Label='ms'}; return \"{0:N2}$Label\" -f $Value")]
    [InlineData("$Label='ms'; $Label+=$Text; return \"{0:N2}$Label\" -f $Value")]
    [InlineData("$Label='ms'; while($Compact){$Result=\"{0:N2}$Label\" -f $Value; $Label=$Text; $Compact=$false}; return $Result")]
    [InlineData("$Label='ms'; foreach($Label in $Labels){$Result=\"{0:N2}$Label\" -f $Value}; return $Result")]
    [InlineData("$Label=$Text.ToString(); return \"{0:N2}$Label\" -f $Value")]
    [InlineData("$Label='N2'; return \"{0:$Label}\" -f $Value")]
    [InlineData("$Label=''; return ('{'+$Label+'{') -f $Value")]
    [InlineData("$Label=''; return ('}'+$Label+'}') -f $Value")]
    [InlineData("return '{1}' -f $Value")]
    [InlineData("return '{0:Q}' -f $Value")]
    [InlineData("return '{0:D}' -f $Value")]
    [InlineData("return '{0:N100}' -f $Value")]
    [InlineData("return '{0,999999999}' -f $Value")]
    [InlineData("return '{{{0:N2}}}' -f $Value")]
    public void RuntimeFreeFormatting_RejectsUnprovenTemplatesAndFlow(string body)
    {
        using var fixture = ArtifactFixture.Create("function Format-Value { param([bool]$Compact,[double]$Value,[string]$Text,[string[]]$Labels); " + body + " }");
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        Assert.Empty(result.Methods);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("separately proven safe template", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData("{0}")]
    [InlineData("{0:}")]
    [InlineData("{0:D2}")]
    [InlineData("{0:X}")]
    [InlineData("{0,12:N0}")]
    [InlineData("{0,-12:N0}")]
    [InlineData("{0: n}", false)]
    [InlineData("{0:N2", false)]
    [InlineData("{0:N2}}", false)]
    [InlineData("{0:N2} }", false)]
    [InlineData("{0:N2} {{text}}")]
    public void RuntimeFreeFormatting_QualifiesTheNumericFormatGrammar(string template, bool accepted = true)
    {
        using var fixture = ArtifactFixture.Create("function Format-Value { param([int]$Value); return '" + template + "' -f $Value }");
        var result = new PowerShellTypedCompilationTranspiler().Transpile(fixture.ScriptPath, targetFramework: "net10.0");
        if (accepted) Assert.Single(result.Methods);
        else Assert.Empty(result.Methods);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void RuntimeFreeFormatting_PreservesNumericValuesAndProvenLabels(string framework, string host)
    {
        var bodies = new[]
        {
            "return '{0:N2}' -f $Value",
            "if($Compact){$Label='ms'}else{$Label=' milliseconds'}; return \"{0:N2}$Label\" -f $Value",
            "if($Compact){$Label='ms'}else{$Label=' milliseconds'}; $Copy=$Label; return \"{0:N2}$Copy\" -f $Value",
            "$Label='m'; $Label+='s'; return \"{0:N2}$Label\" -f $Value",
            "$Label='ms'; return ('{0:N2}'+$Label) -f $Value",
            "[string]$Label=$null; return \"{0:N2}$Label\" -f $Value",
            "return '{{start}} {0:N2} {{end}}' -f $Value",
            "return '{0:N0}/{0:F2}' -f $Value",
            "$Number=0; $Number+=$Value; return '{0:N2}' -f $Number",
            "if($Compact){$Label=''}else{$Label='ms'}; return \"{0:N2}$Label\" -f $Value",
            "$__scalarFormat_0='ms'; $__formatTemplate_1='s'; return \"{0:N2}$__scalarFormat_0$__formatTemplate_1\" -f $Value",
            "return '{0:N2}' -f (Get-CultureValue -Compact $Compact -Value $Value)"
        };
        var source = string.Join(Environment.NewLine, bodies.Select((body, index) =>
            "function Format-Case" + index + " { param([bool]$Compact,[double]$Value); " + body + " }"));
        source += "\nfunction Get-CultureValue { param([bool]$Compact,[double]$Value); [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo('fr-FR'); return $Value }";
        using var fixture = ArtifactFixture.Create(source, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.RuntimeFreeFormat", PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(13, result.Manifest!.CompiledMethods);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
        const string probe = """
            foreach($culture in 'en-US','pl-PL','de-DE') {
                [Threading.Thread]::CurrentThread.CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
                foreach($index in 0..11) { foreach($compact in $false,$true) {
                    foreach($number in 0.0,1234.5,-1.25,[double]::Epsilon,[double]::MinValue,[double]::MaxValue,[double]::NaN,[double]::NegativeInfinity,[double]::PositiveInfinity) {
                        $value=Invoke-Case $index $compact $number
                        [pscustomobject]@{culture=$culture;index=$index;compact=$compact;type=$value.GetType().FullName;value=$value} | ConvertTo-Json -Compress
                    }
                } }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) +
            "'; function Invoke-Case($index,$compact,$number) { & ('Format-Case'+$index) $compact $number }; " + probe,
            fixture.RootPath, "original-runtimefree-format");
        var compiled = RunStatementErrorProbe(host, "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(result.ArtifactPath!) +
            "'); $type=$assembly.GetTypes() | Where-Object { $null -ne $_.GetMethod('Format_Case0') }; " +
            "function Invoke-Case($index,$compact,$number) { $type.GetMethod('Format_Case'+$index).Invoke($null,[object[]]@([bool]$compact,[double]$number)) }; " + probe,
            fixture.RootPath, "compiled-runtimefree-format");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Empty(compiled.StandardError);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(648, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
    }
}
