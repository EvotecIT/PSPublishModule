namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void FailedExecutionRefreshWritesReportWithoutReplacingBaseline()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { 42 }");
        var repository = FindCorpusRepositoryRoot();
        var corpus = Path.Combine(repository, "Benchmarks", "PowerShellCompilation", "Corpus")
            .Replace("'", "''", StringComparison.Ordinal);
        var cli = Environment.GetEnvironmentVariable("POWERFORGE_TEST_CLI_ASSEMBLY_PATH") ??
                  Path.Combine(repository, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
        Assert.True(File.Exists(cli), "The built compiler CLI is required: " + cli);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $root = '{{fixture.RootPath.Replace("'", "''", StringComparison.Ordinal)}}'
            $workspace = Join-Path $root 'workspace'
            $cache = Join-Path $workspace 'payloads'
            New-Item -ItemType Directory -Path $cache | Out-Null
            $source = Join-Path $root 'input.ps1'
            $sha = (Get-FileHash $source).Hash.ToLowerInvariant()
            Copy-Item $source (Join-Path $cache ($sha + '.payload'))
            [IO.File]::WriteAllText((Join-Path $root 'Observer.ps1'), "throw 'Observer must not execute'")
            $entry = [pscustomobject]@{
                id='cached-script';workloadKind='Script';scenarioFamily='ReportFailure';entryPoint='Source.ps1'
                revisionKind='ContentHash';revision=$sha
                acquisition=[pscustomobject]@{kind='File';url='https://example.invalid/Source.ps1';sha256=$sha}
                sourceInputs=@([pscustomobject]@{path='Source.ps1';sha256=$sha})
                qualification=[pscustomobject]@{
                    kind='HybridModuleWorkflow';completeFunctionInventory=$true
                    requiredExports=@('Get-Value');requiredAliases=@()
                    requiredCompiledOwnedFunctions=@([pscustomobject]@{relativePath='Source.ps1';name='Get-Value'})
                    requiredScenarioIds=@('probe');publicExportScenarios=[pscustomobject]@{'Get-Value'=@('probe')}
                    observerScript='Observer.ps1';observerSha256=('0' * 64)
                }
            }
            $packet = [pscustomobject]@{
                schemaVersion=1;packetId='failed-execution-report';semanticProfile='PowerForge.Oracle.PowerShell/7.6'
                targetFramework='net10.0';workloads=@($entry)
            }
            $packetPath = Join-Path $root 'packet.json'
            $packet | ConvertTo-Json -Depth 20 | Set-Content $packetPath
            $baseline = Join-Path $root 'baseline.json'
            $censusReport = Join-Path $root 'census.json'
            $failureReport = Join-Path $root 'failure.json'
            $arguments = @{
                PacketPath=$packetPath;BaselinePath=$baseline;WorkspacePath=$workspace
                CliAssemblyPath='{{cli.Replace("'", "''", StringComparison.Ordinal)}}'
            }
            & '{{corpus}}/Invoke-ExternalAssessment.ps1' @arguments -RefreshBaseline -EvidencePath $censusReport *> (Join-Path $root 'census.log')
            if ($LASTEXITCODE -ne 0) {
                throw ((Get-Content (Join-Path $root 'census.log') -Raw) + (Get-Content $censusReport -Raw))
            }
            $before = (Get-FileHash $baseline).Hash
            & '{{corpus}}/Invoke-ExternalAssessment.ps1' @arguments -ExecuteWorkloads -RefreshBaseline -EvidencePath $failureReport *> (Join-Path $root 'failure.log')
            if ($LASTEXITCODE -ne 1) { throw 'Failed execution did not return failure.' }
            if ((Get-FileHash $baseline).Hash -cne $before) { throw 'Failed execution replaced the baseline.' }
            $report = Get-Content $failureReport -Raw | ConvertFrom-Json
            if ($report.summary.workloadsAssessed -ne 1 -or $report.summary.workloadExecutionsAttempted -ne 1 -or
                $report.summary.workloadExecutionFailures -ne 1 -or $report.summary.completeWorkloadExecutions -ne 0 -or
                $null -eq $report.baselineRefreshError) { throw 'Failure report lost the attempted workload outcome.' }
            $qualificationFiles = @(Get-ChildItem (Join-Path $workspace 'executions') -Recurse -Filter 'cached-script.json')
            if ($qualificationFiles.Count -ne 1) { throw 'The delegated qualification report is missing.' }
            $qualification = Get-Content $qualificationFiles[0].FullName -Raw | ConvertFrom-Json
            if ($qualification.qualifications[0].error -notlike '*Trusted observer differs*') {
                throw 'The fixture failed before reaching the deliberately invalid observer identity.'
            }
            [Console]::Write('failed-execution-reported-and-baseline-preserved')
            exit 0
            """;
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", script);
        Assert.True(run.ExitCode == 0, run.StandardOutput + Environment.NewLine + run.StandardError);
        Assert.Equal("failed-execution-reported-and-baseline-preserved", run.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void AssessmentAcquisitionSeparatesSharedPayloadsAndBindsPacketBytes()
    {
        using var fixture = ArtifactFixture.Create("function Get-Value { 42 }");
        var corpus = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus")
            .Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{corpus}}/Corpus.Runner.Common.ps1'
            . '{{corpus}}/Corpus.Assessment.Acquisition.ps1'
            $root = '{{fixture.RootPath.Replace("'", "''", StringComparison.Ordinal)}}'
            $payload = Join-Path $root 'input.ps1'
            $sha = (Get-FileHash $payload).Hash.ToLowerInvariant()
            $roots = @(foreach ($id in 'A','B') {
                $entry = [pscustomobject]@{
                    id=$id;entryPoint="$id.ps1"
                    acquisition=[pscustomobject]@{kind='File';sha256=$sha}
                }
                Get-AssessmentSourceRoot -Entry $entry -PayloadPath $payload -ExtractCache (Join-Path $root 'extract')
            })
            if ($roots[0] -eq $roots[1]) { throw 'Two workloads share an extraction root.' }
            foreach ($index in 0,1) {
                $id = @('A','B')[$index]
                if ((Get-FileHash (Join-Path $roots[$index] "$id.ps1")).Hash.ToLowerInvariant() -cne $sha) {
                    throw 'A later extraction replaced an earlier workload input.'
                }
            }
            $packetPath = Join-Path $root 'packet.json'
            [IO.File]::WriteAllText($packetPath, '{"schemaVersion":1,"packetId":"before","workloads":[]}')
            $snapshot = Read-AssessmentPacket $packetPath
            if ($snapshot.Sha256 -cne (Get-FileHash $packetPath).Hash.ToLowerInvariant()) { throw 'Packet hash differs from its parsed bytes.' }
            [IO.File]::WriteAllText($packetPath, '{"schemaVersion":1,"packetId":"after","workloads":[]}')
            if ($snapshot.Packet.packetId -cne 'before') { throw 'Parsed packet identity changed.' }
            $workspace = Join-Path $root 'execution'
            $rejected = $false
            try {
                & '{{corpus}}/Invoke-ExternalQualification.ps1' -PacketPath $packetPath -ExpectedPacketSha256 $snapshot.Sha256 `
                    -WorkspacePath $workspace -AllowExternalExecution
            } catch {
                if ($_.Exception.Message -cne 'Qualification packet differs from the assessment packet identity.') { throw }
                $rejected = $true
            }
            if (-not $rejected -or (Test-Path $workspace)) { throw 'A changed packet reached execution setup.' }
            [Console]::Write('separate-source-roots-and-pinned-packet')
            """;
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", script);
        Assert.True(run.ExitCode == 0, run.StandardOutput + Environment.NewLine + run.StandardError);
        Assert.Equal("separate-source-roots-and-pinned-packet", run.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void AssessmentRejectsUnsafeIdsBeforeCreatingExecutionEvidence()
    {
        using var fixture = ArtifactFixture.Create(string.Empty);
        var corpus = Path.Combine(FindCorpusRepositoryRoot(), "Benchmarks", "PowerShellCompilation", "Corpus")
            .Replace("'", "''", StringComparison.Ordinal);
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $root = '{{fixture.RootPath.Replace("'", "''", StringComparison.Ordinal)}}'
            $packet = Join-Path $root 'packet.json'
            foreach ($unsafeId in '../escape', 'A.', 'CON', 'nul.txt', 'COM1', 'LPT9.log', "A`n") {
                [pscustomobject]@{schemaVersion=1;workloads=@([pscustomobject]@{id=$unsafeId})} |
                    ConvertTo-Json -Depth 4 | Set-Content $packet
                foreach ($runner in 'Invoke-ExternalAssessment.ps1','Invoke-ExternalQualification.ps1') {
                    $workspace = Join-Path $root $runner
                    $arguments = @{PacketPath=$packet;WorkspacePath=$workspace}
                    if ($runner -eq 'Invoke-ExternalQualification.ps1') { $arguments.AllowExternalExecution=$true }
                    $rejected = $false
                    try { & (Join-Path '{{corpus}}' $runner) @arguments }
                    catch {
                        if ($_.Exception.Message -cne 'Assessment workload id must be a portable filename component.') { throw }
                        $rejected = $true
                    }
                    if (-not $rejected -or (Test-Path $workspace)) { throw 'An unsafe ID reached filesystem setup.' }
                }
            }
            [Console]::Write('unsafe-ids-rejected-before-writes')
            """;
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", script);
        Assert.True(run.ExitCode == 0, run.StandardOutput + Environment.NewLine + run.StandardError);
        Assert.Equal("unsafe-ids-rejected-before-writes", run.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

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
                foreach ($arrayLength in 2,1) {
                    $combined = [ordered]@{relativePath='Helpers.ps1';name='Choice'}
                    $combined[$selector] = @($required | Select-Object -First $arrayLength | ForEach-Object { $_.$selector })
                    $report.scenarios[0].executedOwnedFunctions = @([pscustomobject]$combined)
                    $report.scenarios[1].executedOwnedFunctions = @($required[1])
                    $rejected = $false
                    try { Assert-WorkflowObservation $contract $report 'Built' $PWD.Path }
                    catch {
                        if ($_.Exception.Message -notlike "Executed function identity requires * '$selector'.") { throw }
                        $rejected = $true
                    }
                    if (-not $rejected) { throw "One array-valued $selector satisfied declaration identities." }
                }
            }
            [Console]::Write('complete-inventory-and-execution')
            """;
        var run = Run("pwsh", "-NoProfile", "-NonInteractive", "-Command", script);
        Assert.True(run.ExitCode == 0, run.StandardOutput + Environment.NewLine + run.StandardError);
        Assert.Equal("complete-inventory-and-execution", run.StandardOutput);
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }
}
