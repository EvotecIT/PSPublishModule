namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void CompleteCorpusWorkloadCreditRequiresEveryFunctionAndExecutionObservation()
    {
        var workflow = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus",
            "Corpus.Qualification.Workflow.ps1").Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            . (Join-Path (Split-Path '{{workflow}}') 'Corpus.Runner.Common.ps1')
            . '{{workflow}}'
            $units = @(foreach ($name in 'One', 'Two') {
                [pscustomobject]@{unitId=$name; relativePath='Helpers.ps1'; name=$name; kind='Function';
                    emittedClrMethod=$true; retainedHostedSource=$false; omitted=$false; rejected=$false; shapingFallback=$false}
            })
            $required = @($units | ForEach-Object { [pscustomobject]@{relativePath=$_.relativePath; name=$_.name} })
            $contract = [pscustomobject]@{
                completeFunctionInventory=$true
                requiredCompiledOwnedFunctions=@($required[0])
                requiredExports=@('One','Two')
                requiredAliases=@()
                requiredScenarioIds=@('first','second')
                publicExportScenarios=[pscustomobject]@{One=@('first');Two=@('second')}
            }
            $rejected = $false
            try { $null = Assert-WorkflowCompiledOwnership $contract ([pscustomobject]@{entries=$units}) }
            catch {
                if ($_.Exception.Message -notlike 'Complete owned function inventory differs.*') { throw }
                $rejected = $true
            }
            if (-not $rejected) { throw 'An incomplete function inventory received complete-workload credit.' }
            $contract.requiredCompiledOwnedFunctions = $required
            $owned = @(Assert-WorkflowCompiledOwnership $contract ([pscustomobject]@{entries=$units}))
            if ($owned.Count -ne 2) { throw 'The complete inventory lost an emitted function.' }
            $report = [pscustomobject]@{
                schemaVersion=1;surface='Built';importPath=$PWD.Path
                actualExports=@('One','Two');actualAliases=@()
                scenarios=@(
                    [pscustomobject]@{scenarioId='first';succeeded=$true;executedExports=@('One');
                        executedOwnedFunctions=@($required[0]);observation='one'},
                    [pscustomobject]@{scenarioId='second';succeeded=$true;executedExports=@('Two');
                        executedOwnedFunctions=@();observation='two'}
                )
            }
            $rejected = $false
            try { Assert-WorkflowObservation $contract $report 'Built' $PWD.Path }
            catch {
                if ($_.Exception.Message -cne "Built did not execute owned function 'Two'.") { throw }
                $rejected = $true
            }
            if (-not $rejected) { throw 'An unexecuted function received complete-workload credit.' }
            $report.scenarios[1].executedOwnedFunctions = @($required[1])
            Assert-WorkflowObservation $contract $report 'Built' $PWD.Path
            foreach ($selector in 'unitId', 'startLine') {
                $units[0].name = $units[1].name = 'Choice'
                $units[0] | Add-Member startLine 10 -Force
                $units[1] | Add-Member startLine 20 -Force
                $required = @($units | ForEach-Object {
                    $identity = [ordered]@{relativePath=$_.relativePath;name=$_.name}
                    $identity[$selector] = $_.$selector
                    [pscustomobject]$identity
                })
                $contract.requiredCompiledOwnedFunctions = $required
                $contract.requiredExports = @('Choice')
                $contract.publicExportScenarios = [pscustomobject]@{Choice=@('first','second')}
                $null = Assert-WorkflowCompiledOwnership $contract ([pscustomobject]@{entries=$units})
                $report.actualExports = @('Choice')
                foreach ($scenario in $report.scenarios) { $scenario.executedExports = @('Choice') }
                foreach ($observed in @($required[0], [pscustomobject]@{relativePath='Helpers.ps1';name='Choice'})) {
                    foreach ($scenario in $report.scenarios) { $scenario.executedOwnedFunctions = @($observed) }
                    $rejected = $false
                    try { Assert-WorkflowObservation $contract $report 'Built' $PWD.Path }
                    catch {
                        if ($_.Exception.Message -cne "Built did not execute owned function 'Choice'.") { throw }
                        $rejected = $true
                    }
                    if (-not $rejected) { throw "An unobserved or ambiguous $selector received complete-workload credit." }
                }
                $report.scenarios[0].executedOwnedFunctions = @($required[0])
                $report.scenarios[1].executedOwnedFunctions = @($required[1])
                Assert-WorkflowObservation $contract $report 'Built' $PWD.Path
            }
            [Console]::Write('complete-inventory-and-execution')
            """;
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", script);
        Assert.True(run.ExitCode == 0, run.StandardOutput + Environment.NewLine + run.StandardError);
        Assert.Equal("complete-inventory-and-execution", run.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }
}
