using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void FinallyErrors_PreservePreferencesCaptureAndStop(string framework, string host)
    {
        using var fixture = ArtifactFixture.Create("""
            function Invoke-Cleanup {
                [CmdletBinding()] param([int]$Capacity,[Collections.ArrayList]$Trace)
                [void]$Trace.Add('entered')
                [void][Collections.ArrayList]::new($Capacity)
                [void]$Trace.Add('cleaned')
            }
            function Get-DirectCleanup {
                [CmdletBinding()] param([int]$Capacity,[Collections.ArrayList]$Trace)
                try { 'first'; 'second' }
                finally {
                    [void]$Trace.Add('entered')
                    [void][Collections.ArrayList]::new($Capacity)
                    [void]$Trace.Add('cleaned')
                }
                'after'
            }
            function Get-IndirectCleanup {
                [CmdletBinding()] param([int]$Capacity,[Collections.ArrayList]$Trace)
                try { 'first'; 'second' }
                finally { Invoke-Cleanup -Capacity $Capacity -Trace $Trace }
                'after'
            }
            """, ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.FinallyErrors", PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict, allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(3, result.Manifest!.CompiledMethods);
        Assert.Equal(0, result.Manifest.RuntimeFallbackUnits);
        const string probe = """
            function Describe-Record($record) {
                if ($record -is [Management.Automation.ErrorRecord]) {
                    return @{kind='error';id=$record.FullyQualifiedErrorId;type=$record.Exception.GetType().FullName;category=$record.CategoryInfo.Category.ToString()}
                }
                if ($record -is [Exception]) {
                    return @{kind='exception';type=$record.GetType().FullName;message=$record.Message}
                }
                return @{kind='value';type=$(if ($null -eq $record) {'null'} else {$record.GetType().FullName});value=[string]$record}
            }
            foreach ($command in 'Get-DirectCleanup','Get-IndirectCleanup') {
                foreach ($capacity in -1,1) {
                    foreach ($action in 'Continue','SilentlyContinue','Stop') {
                        foreach ($stop in $false,$true) {
                            $trace=[Collections.ArrayList]::new()
                            $records=[Collections.Generic.List[object]]::new()
                            $faults=@()
                            $parameters=@{Capacity=$capacity;Trace=$trace;ErrorAction=$action;ErrorVariable='faults'}
                            if ($action -eq 'Stop') {
                                try {
                                    if ($stop) { & $command @parameters 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                    else { & $command @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                                } catch { [void]$records.Add((Describe-Record $_)) }
                            } elseif ($stop) { & $command @parameters 2>&1 | Select-Object -First 1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            else { & $command @parameters 2>&1 | ForEach-Object { [void]$records.Add((Describe-Record $_)) } }
                            [pscustomobject]@{command=$command;capacity=$capacity;action=$action;stop=$stop;records=$records.ToArray();
                                trace=$trace.ToArray();errors=@($faults | ForEach-Object { Describe-Record $_ })} | ConvertTo-Json -Depth 10 -Compress
                        }
                    }
                }
            }
            """;
        var original = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(fixture.ScriptPath) + "'; " + probe,
            fixture.RootPath, "original-finally-errors");
        var compiled = RunStatementErrorProbe(host, "Import-Module '" + EscapeStatementErrorPath(result.ArtifactPath!) + "'; " + probe,
            fixture.RootPath, "compiled-finally-errors");
        Assert.Equal(0, original.ExitCode);
        Assert.Empty(original.StandardError);
        Assert.Contains("ArgumentOutOfRangeException", original.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("cleaned", original.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(original.ExitCode, compiled.ExitCode);
        // Hashtable property order is host/process-dependent; compare JSON structure.
        var originals = original.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var generated = compiled.StandardOutput.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(24, originals.Length);
        Assert.Equal(originals.Length, generated.Length);
        for (var index = 0; index < originals.Length; index++)
            Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
                System.Text.Json.Nodes.JsonNode.Parse(originals[index]), System.Text.Json.Nodes.JsonNode.Parse(generated[index])),
                "Original: " + originals[index] + Environment.NewLine + "Compiled: " + generated[index]);
        Assert.Equal(original.StandardError, compiled.StandardError);
    }
}
