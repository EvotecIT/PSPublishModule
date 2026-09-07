namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    public static IEnumerable<object[]> LifecycleArgumentHosts()
        => StatementErrorHosts().Where(static host => !Equals(host[0], "net472"));

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(LifecycleArgumentHosts))]
    public void RuntimeFreeLifecycle_PreservesSwitchArgumentsAndOptionalEnd(string framework, string host)
    {
        var source = string.Join(Environment.NewLine, new[] { "", "end { }", "end { 'done' }" }.Select((end, index) =>
            "function Format-C" + index + " { [CmdletBinding()] [OutputType([string])] " +
            "param([switch]$Compact,[Parameter(ValueFromPipeline)][int]$Value,[switch]$Duplicate) " +
            "begin { if($Compact){$Prefix='v'}else{$Prefix='value:'} } " +
            "process { \"$Prefix$Value\"; if($Duplicate){\"$Prefix$Value\"} } " + end + " } " +
            "function Invoke-FlagsC" + index + " { param([int[]]$Values,[bool]$Compact,[bool]$Duplicate) " +
            "$Values | Format-C" + index + " -Duplicate:$Duplicate -Compact:$Compact } " +
            "function Invoke-DefaultC" + index + " { param([int[]]$Values) $Values | Format-C" + index + " } " +
            "function Invoke-CompactC" + index + " { param([int[]]$Values) $Values | Format-C" + index + " -Compact }"));
        using var fixture = ArtifactFixture.Create(source + "\n1,2 | Format-C0");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.LifecycleArguments", PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = framework,
            SingleFile = false
        });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(13, result.Manifest!.CompiledMethods); // Twelve functions and the executable entry method.
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        Assert.False(result.Manifest.ContainsEmbeddedPowerShellSource);
        Assert.False(result.Manifest.AllowsPowerShellRuntimeEvaluation);
        var assemblyPath = Assert.Single(result.Manifest.Files, static file => file.Role == "GeneratedAssembly").Path;
        const string probe = """
            $cases=[Collections.Generic.List[object]]::new()
            $cases.Add($null); $cases.Add([int[]]@()); $cases.Add([int[]]@(7)); $cases.Add([int[]]@(-1,0,2,[int]::MinValue,[int]::MaxValue))
            foreach($index in 0..2) { foreach($variant in 'Flags','Default','Compact') {
                foreach($compact in $false,$true) { foreach($duplicate in $false,$true) {
                    foreach($case in 0..3) {
                        [int[]]$values=$cases[$case]
                        $records=@(Invoke-Case $index $variant $values $compact $duplicate)
                        [pscustomobject]@{index=$index;variant=$variant;compact=$compact;duplicate=$duplicate;case=$case;count=$records.Count;types=@($records | ForEach-Object {$_.GetType().FullName});values=$records} | ConvertTo-Json -Compress
                    }
                } }
            } }
            """;
        var original = RunStatementErrorProbe(host,
            "$null=. '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " +
            "function Invoke-Case($index,$variant,$values,$compact,$duplicate) { " +
            "if($variant -eq 'Flags'){& ('Invoke-'+$variant+'C'+$index) -Values $values -Compact $compact -Duplicate $duplicate} " +
            "else{& ('Invoke-'+$variant+'C'+$index) -Values $values} }; " + probe,
            fixture.RootPath, "original-lifecycle-arguments");
        var compiled = RunStatementErrorProbe(host,
            "$assembly=[Reflection.Assembly]::LoadFrom('" + EscapeStatementErrorPath(assemblyPath) + "'); " +
            "$type=$assembly.GetTypes() | Where-Object {$null -ne $_.GetMethod('Invoke_FlagsC0')}; " +
            "function Invoke-Case($index,$variant,$values,$compact,$duplicate) { " +
            "$method=$type.GetMethod('Invoke_'+$variant+'C'+$index); " +
            "if($method.ReturnType.FullName -ne 'System.String[]'){throw 'Expected materialized lifecycle output'}; " +
            "if($variant -eq 'Flags'){$method.Invoke($null,[object[]]@($values,[bool]$compact,[bool]$duplicate))} " +
            "else{$arguments=[object[]]::new(1);$arguments[0]=$values;$method.Invoke($null,$arguments)} }; " + probe,
            fixture.RootPath, "compiled-lifecycle-arguments");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.Empty(original.StandardError);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        Assert.Empty(compiled.StandardError);
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(144, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
    }
}
