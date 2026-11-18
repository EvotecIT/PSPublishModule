using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationSemanticOracleTests
{
    [PinnedSemanticHostFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void IndependentHybridCommandsPreserveBindingPipelinesAndLifecycleBesideHostedTypes()
    {
        const string source = """
            class RetainedRecord {
                [int] $Value
                [int] Read() { return $this.Value }
            }
            $script:BeforeRecords = @(Get-Command -Name Read-Records -ErrorAction Ignore).Count
            function New-RetainedRecord {
                $record = [RetainedRecord]::new()
                $record.Value = 7
                $record
            }
            function Read-Records {
                [CmdletBinding()]
                param(
                    [Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)]
                    [Alias('v')]
                    [ValidateRange(0, 10)]
                    [int] $Value
                )
                begin { $total = 0; Write-Output 'begin' }
                process {
                    $total += $Value
                    $Value | ForEach-Object { Write-Output "item=$_" }
                    Write-Verbose "processed=$Value"
                }
                end { Write-Output "end=$total" }
            }
            function Read-Commands {
                [CmdletBinding()]
                param([string] $Name)
                Get-Command -Name $Name | Select-Object -ExpandProperty Name
            }
            function ReadValues($Values) { $Values | ForEach-Object { $_ } }
            function Read-RetainedRecord {
                $record = New-RetainedRecord
                $record.Read()
            }
            function Read-DynamicRecord {
                $record = New-Object -TypeName RetainedRecord
                $record.Value = 9
                $record.Read()
            }
            function Read-StoppingRecords {
                [CmdletBinding()]
                param([Parameter(ValueFromPipeline)][int] $Value, [object] $Trace)
                begin { [void] $Trace.Add('begin') }
                process {
                    try { Write-Output "stop=$Value" }
                    finally { [void] $Trace.Add("finally=$Value") }
                }
                end { [void] $Trace.Add('end') }
            }
            Export-ModuleMember -Function New-RetainedRecord, Read-Records, Read-Commands, ReadValues, Read-RetainedRecord, Read-DynamicRecord, Read-StoppingRecords
            """;
        const string probe = """
            $rows = @(
                ,@(Read-Records -v 2)
                ,@(@() | Read-Records)
                ,@(3 | Read-Records)
                ,@(1, 2, 3 | Read-Records)
                ,@([pscustomobject]@{v=4}, [pscustomobject]@{Value=5} | Read-Records)
                ,@(Read-Records -Val 6 -Verbose 4>&1 | ForEach-Object { $_.ToString() })
                ,@(Read-Commands -Name Write-Output)
                ,@(ReadValues -Values @(8, 9))
                ,@(Read-RetainedRecord)
                ,@(Read-DynamicRecord)
            )
            $failures = @(foreach ($bad in -1, 11) {
                try { Read-Records -Value $bad -ErrorAction Stop }
                catch { $_.FullyQualifiedErrorId + '|' + $_.CategoryInfo.Category }
            })
            $afterFailure = @(7 | Read-Records)
            $partial = @(1, 11, 2 | Read-Records -ErrorAction Continue 2>&1 | ForEach-Object {
                if ($_ -is [System.Management.Automation.ErrorRecord]) { $_.FullyQualifiedErrorId + '|' + $_.CategoryInfo.Category }
                else { $_ }
            })
            $module = Get-Module | Where-Object Path -eq $modulePath
            $identity = @($module.Invoke({ (New-RetainedRecord).GetType() -eq [RetainedRecord] }))[0]
            $before = @($module.Invoke({ $script:BeforeRecords }))[0]
            $trace = [Collections.Generic.List[string]]::new()
            $stopped = @(1, 2, 3 | Read-StoppingRecords -Trace $trace | Select-Object -First 1)
            [pscustomobject]@{rows=$rows; failures=$failures; afterFailure=$afterFailure; partial=$partial; identity=$identity; before=$before; stopped=$stopped; trace=@($trace)} |
                ConvertTo-Json -Depth 8 -Compress
            """;
        var modernHost = Environment.GetEnvironmentVariable("POWERFORGE_PWSH76_PATH");
        Assert.False(string.IsNullOrWhiteSpace(modernHost), "The exact PowerShell 7.6.6 host is required.");
        foreach (var (framework, profile, host) in new[]
                 {
                     ("net10.0", PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId, modernHost),
                     ("net472", PowerShellCompilationSemanticOracleCatalog.WindowsPowerShell51ProfileId, (string?)null)
                 })
        {
            using var fixture = OracleFixture.Create(string.Empty);
            var modulePath = Path.Combine(fixture.RootPath, "commands.psm1");
            File.WriteAllText(modulePath, source);
            var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                modulePath, Path.Combine(fixture.RootPath, "built"), "HostedTypeIndependentCommands",
                PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
                allowUnreviewedDependencyResolution: true)
            {
                TargetFramework = framework,
                SemanticProfileId = profile,
                EmitSource = true
            });
            Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
            foreach (var name in new[] { "Read-Records", "Read-Commands", "ReadValues", "Read-RetainedRecord", "Read-DynamicRecord", "Read-StoppingRecords" })
            {
                var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
                Assert.True(unit.EmittedClrMethod, name + " must have a complete CLR body.");
                Assert.False(unit.RetainedHostedSource, name + " must not use an authored source fallback.");
            }
            var retained = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries,
                static unit => unit.Name == "New-RetainedRecord");
            Assert.False(retained.EmittedClrMethod);
            Assert.True(retained.RetainedHostedSource);

            var runner = new PowerShellCompilationSemanticOracleRunner();
            PowerShellCompilationSemanticOracleEnvelope Observe(string path)
            {
                File.WriteAllText(fixture.ScriptPath,
                    "$modulePath='" + path.Replace("'", "''", StringComparison.Ordinal) + "'\n" +
                    "$global:VerbosePreference='SilentlyContinue'\n" +
                    "Import-Module Microsoft.PowerShell.Utility -Verbose:$false\n" +
                    "Import-Module -Name $modulePath -Force -ErrorAction Stop -Verbose:$false\n" +
                    "$global:VerbosePreference='Continue'\n" + probe);
                var pin = PowerShellCompilationSemanticHostArtifactPinCatalog.Get(profile);
                return runner.Observe(new PowerShellCompilationSemanticOracleRequest(profile, fixture.ScriptPath)
                {
                    HostExecutablePath = host,
                    ExpectedHostArtifactSha256 = PowerShellCompilationSemanticTestHost.GetIdentity(pin)
                });
            }
            var original = Observe(modulePath);
            var generated = Observe(built.ArtifactPath!);
            Assert.Empty(original.ErrorRecords);
            Assert.Empty(generated.ErrorRecords);
            var result = Assert.Single(original.Success);
            Assert.Contains("item=3", result.Value, StringComparison.Ordinal);
            Assert.Contains("end=6", result.Value, StringComparison.Ordinal);
            Assert.Contains("ParameterArgumentValidationError", result.Value, StringComparison.Ordinal);
            Assert.Contains("\"identity\":true", result.Value, StringComparison.Ordinal);
            Assert.Contains("\"before\":0", result.Value, StringComparison.Ordinal);
            var differences = PowerShellCompilationSemanticOracleComparer.Compare(original, generated);
            Assert.True(differences.Count == 0,
                string.Join(Environment.NewLine, differences.Select(static difference =>
                    difference.Path + ": original=" + difference.Expected + "; generated=" + difference.Actual)));
        }
    }
}
