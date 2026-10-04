namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void NativeLifecycle_PreservesDefaultsValidationParameterSetsAndCallbacks(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Read-LifecycleBinding {
                [CmdletBinding(DefaultParameterSetName='Input')] param(
                    [object]$Trace,
                    [Parameter(ValueFromPipeline,ValueFromPipelineByPropertyName,ParameterSetName='Input')]
                    [ValidateScript({$Trace.Add('validate:'+$_); $_ -ne 'bad'})][string]$Value=$($Trace.Add('default'); 'default'),
                    [Parameter(ParameterSetName='Other')][switch]$Other)
                begin { $Trace.Add('begin:'+ $Value + ':' + $PSBoundParameters.ContainsKey('Value')); 'begin:'+ $PSCmdlet.ParameterSetName }
                process { $Trace.Add('process:'+ $Value); 'value:'+ $Value + ':' + $PSBoundParameters.ContainsKey('Value') }
                end { $Trace.Add('end:'+ $Value); 'end:'+ $Value }
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.NativeLifecycleBinding", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.All(result.Manifest.UnitDispositionLedger!.Entries, unit => Assert.False(unit.RetainedHostedSource));
        const string probe = """
            function Describe-BindingError($value) {
                $record=if($value -is [Management.Automation.ErrorRecord]) {$value} else {$value.ErrorRecord}
                if($null -eq $record) {return $value.GetType().FullName}
                [pscustomobject]@{id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;category=[string]$record.CategoryInfo.Category}
            }
            foreach($style in 'direct','other','value','property') {
                foreach($shape in 'empty','many','invalid','callback') {
                    foreach($action in 'Continue','SilentlyContinue','Stop') {
                        $trace=[Collections.Generic.List[string]]::new();$errors=@();$caught=$null;$records=[Collections.Generic.List[string]]::new()
                        $values=switch($shape) {
                            'empty' {,@()}; 'many' {,@('a','b')}; 'invalid' {,@('a','bad','c')}
                            'callback' {
                                $item=[pscustomobject]@{Trace=$trace}
                                $item | Add-Member ScriptMethod ToString {$this.Trace.Add('convert:'+ $Value); 'callback'} -Force
                                ,@($item,'last')
                            }
                        }
                        if($style -eq 'property') {$values=@($values | ForEach-Object {[pscustomobject]@{Value=$_}})}
                        try {
                            if($style -eq 'direct') {Read-LifecycleBinding -Trace $trace -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)}}
                            elseif($style -eq 'other') {Read-LifecycleBinding -Other -Trace $trace -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)}}
                            else {$values | Read-LifecycleBinding -Trace $trace -ErrorAction $action -ErrorVariable errors 2>$null | ForEach-Object {$records.Add($_)}}
                        } catch {$caught=Describe-BindingError $_}
                        [pscustomobject]@{style=$style;shape=$shape;action=$action;records=@($records);trace=@($trace);
                            errors=@($errors | ForEach-Object {Describe-BindingError $_});caught=$caught} | ConvertTo-Json -Compress -Depth 10
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-native-lifecycle-binding");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-native-lifecycle-binding");
        Assert.True(original.ExitCode == 0, original.StandardOutput + original.StandardError);
        Assert.True(compiled.ExitCode == 0, compiled.StandardOutput + compiled.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
        var expected = original.StandardOutput.Split('\n');
        var actual = compiled.StandardOutput.Split('\n');
        Assert.Equal(48, expected.Count(line => !string.IsNullOrWhiteSpace(line)));
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.SequenceEqual(actual), string.Join(Environment.NewLine, expected.Zip(actual)
            .Where(pair => pair.First != pair.Second).Take(8).Select(pair => "Original: " + pair.First + Environment.NewLine + "Generated: " + pair.Second)));
    }
}
