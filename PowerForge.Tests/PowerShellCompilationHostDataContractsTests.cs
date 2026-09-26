using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData("net10.0")]
    [InlineData("net472")]
    public void HostDataContracts_RemainRejectedWithoutPowerShellHostTypes(string framework)
    {
        foreach (var source in new[]
        {
            "function Read-Record { param([System.Management.Automation.ErrorRecord[]]$Value) return $Value }",
            "function Read-Stop { param([System.Management.Automation.ActionPreferenceStopException[]]$Value) return $Value }",
            "function Read-Category { param([System.Management.Automation.ErrorCategory]$Value) return $Value }",
            "function Read-Serialized { param([string]$Value) return [System.Management.Automation.PSSerializer]::Deserialize($Value) }"
        })
        {
            using var fixture = ArtifactFixture.Create(source);
            var plan = new PowerShellCompilationAnalyzer().Analyze(new PowerShellCompilationSpec(
                fixture.ScriptPath, PowerShellCompilationMode.Strict, targetFramework: framework,
                capabilities: PowerShellCompilationCapabilities.TypedExecutable));
            Assert.False(Assert.Single(Assert.Single(plan.Files).Units).IsCompilable);
        }
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [MemberData(nameof(StatementErrorHosts))]
    public void PinnedHostDataContracts_PreserveSerializationAndErrorProjection(string framework, string host)
    {
        var sources = new[]
        {
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Objects", "Copy-Dictionary.ps1"),
            FindCompleteConversionWorkflow("PSSharedGoods", "FullModule", "Public", "Converts", "ConvertFrom-ErrorRecord.ps1"),
            FindCompleteConversionWorkflow("CleanupMonster", "FullModule", "Private", "Test-ADQueryConfigurationError.ps1")
        };
        using var fixture = ArtifactFixture.Create(string.Join(Environment.NewLine, sources.Select(File.ReadAllText)) + """

            function New-OfflineError {
                [CmdletBinding()] param([ValidateRange(1,9)][int]$Seed=1)
                return [System.Management.Automation.ErrorRecord]::new(
                    [System.InvalidOperationException]::new('offline error'), 'OfflineError',
                    [System.Management.Automation.ErrorCategory]::InvalidOperation, 'owned-target')
            }
            function New-OfflineStop {
                [CmdletBinding()] param([System.Management.Automation.ErrorRecord]$Record)
                return [System.Management.Automation.ActionPreferenceStopException]::new($Record)
            }
            """, ".psm1");
        var built = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath, fixture.OutputPath, "Generated.HostDataContracts",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true) { TargetFramework = framework });
        Assert.True(built.Succeeded, built.Error + Environment.NewLine + built.BuildOutput);
        foreach (var name in new[] { "Copy-Dictionary", "ConvertFrom-ErrorRecord", "Test-ADQueryConfigurationError", "New-OfflineError", "New-OfflineStop" })
        {
            var unit = Assert.Single(built.Manifest!.UnitDispositionLedger!.Entries, unit => unit.Name == name);
            Assert.True(unit.EmittedClrMethod, System.Text.Json.JsonSerializer.Serialize(unit));
        }
        const string probe = """
            $inputMap=[ordered]@{name='root';nested=@{name='child'};items=@(1,'two');object=[pscustomobject]@{name='note'}}
            foreach($command in 'Copy-Dictionary','Copy-Hashtable','Copy-OrderedHashtable') {
                $copy=& $command -Dictionary $inputMap
                $copy.nested.name='changed'
                $copy.object.name='changed'
                [pscustomobject]@{command=$command;type=$copy.GetType().FullName;keys=@($copy.Keys)
                    same=[object]::ReferenceEquals($copy,$inputMap);original=$inputMap.nested.name
                    originalNote=$inputMap.object.name;copy=$copy.nested.name;items=@($copy.items)
                }|ConvertTo-Json -Compress -Depth 5
            }
            $errorRecord=New-OfflineError
            $stop=New-OfflineStop -Record $errorRecord
            foreach($case in 'direct','array','pipeline','stop','stop-pipeline') {
                $observed=switch($case) {
                    direct {ConvertFrom-ErrorRecord -ErrorRecord $errorRecord}
                    array {ConvertFrom-ErrorRecord -ErrorRecord @($errorRecord,$errorRecord)}
                    pipeline {$errorRecord,$errorRecord|ConvertFrom-ErrorRecord}
                    stop {ConvertFrom-ErrorRecord -Exception $stop}
                    stop-pipeline {$stop|ConvertFrom-ErrorRecord}
                }
                [pscustomobject]@{case=$case;records=@($observed);types=@($observed|ForEach-Object {$_.GetType().FullName})}|ConvertTo-Json -Compress -Depth 6
            }
            foreach($message in 'The search filter cannot be recognized','prefix THE SEARCH FILTER CANNOT BE RECOGNIZED suffix','connection failed','') {
                $record=[System.Management.Automation.ErrorRecord]::new(
                    [System.InvalidOperationException]::new($message),'OfflineClassification',
                    [System.Management.Automation.ErrorCategory]::ConnectionError,'owned-target')
                [pscustomobject]@{message=$message;configuration=(Test-ADQueryConfigurationError -ErrorRecord $record)}|ConvertTo-Json -Compress
            }
            try {Copy-Dictionary -Dictionary 42 -ErrorAction Stop}
            catch {[pscustomobject]@{bindingError=$_.FullyQualifiedErrorId;type=$_.Exception.GetType().FullName}|ConvertTo-Json -Compress}
            """;
        var original = RunModuleProof(fixture.ScriptPath, probe, host);
        var generated = RunModuleProof(built.ArtifactPath!, probe, host);
        Assert.Equal(original, generated);
        Assert.Contains("\"same\":false", generated);
        Assert.Contains("\"original\":\"child\"", generated);
        Assert.Contains("\"originalNote\":\"note\"", generated);
        Assert.Contains("\"copy\":\"changed\"", generated);
        Assert.Contains("offline error", generated);
        Assert.Contains("owned-target", generated);
        Assert.Contains("\"configuration\":true", generated);
        Assert.Contains("\"configuration\":false", generated);
    }
}
