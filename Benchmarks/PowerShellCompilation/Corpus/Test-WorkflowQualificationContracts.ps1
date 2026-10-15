[CmdletBinding()]
param([Parameter(Mandatory)] [string] $WorkspacePath, [string] $EvidencePath,
    [ValidateSet('All', 'ArtifactName', 'DurableEvidence', 'NestedEmission', 'OfflineMode')] [string] $Scope = 'All',
    [string] $MetadataAssemblyPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Qualification.Workflow.ps1')
$checked = [Collections.Generic.List[string]]::new()

function Copy-ContractValue {
    param($Value)
    ConvertTo-Json -InputObject $Value -Depth 30 | ConvertFrom-Json
}

function Assert-RejectedContract {
    param([string] $Name, [scriptblock] $Action, [string] $Message)
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw "$Name rejected for an unexpected reason: $($_.Exception.Message)" }
        $checked.Add($Name)
        return
    }
    throw "$Name accepted a broken qualification contract."
}

Assert-WorkflowArtifactName 'powershell-yaml'
Assert-WorkflowArtifactName 'platyPS'
Assert-WorkflowArtifactName $null
$checked.Add('module artifact basename preserves public identity and optional default')
Assert-RejectedContract 'artifact basename traversal' {Assert-WorkflowArtifactName '../module'} 'portable basename'
Assert-RejectedContract 'artifact basename invalid portable characters' {Assert-WorkflowArtifactName 'module:other'} 'portable basename'
Assert-RejectedContract 'artifact basename must be a string' {Assert-WorkflowArtifactName 3} 'portable basename'
if($Scope -eq 'ArtifactName') {
    $result=[ordered]@{schemaVersion=1;passed=$true;checks=@($checked);checkCount=$checked.Count;scope='ArtifactName assertions only; no process/compiler/SDK/workload execution'}
    if($EvidencePath) {Write-Utf8Json -Path $EvidencePath -Value $result}
    $result|ConvertTo-Json -Depth 8
    return
}

if($Scope -eq 'NestedEmission') {
    if(-not $MetadataAssemblyPath -or -not(Test-Path -LiteralPath $MetadataAssemblyPath -PathType Leaf)) {
        throw 'NestedEmission proof requires an existing frozen managed MetadataAssemblyPath; it builds nothing.'
    }
    $proofRoot=Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) ('nested-'+[guid]::NewGuid().ToString('N'))
    $outputRoot=Join-Path $proofRoot 'output'
    New-Item -ItemType Directory -Path $outputRoot|Out-Null
    $assembly=Join-Path $outputRoot 'frozen-metadata.dll'
    Copy-Item -LiteralPath $MetadataAssemblyPath -Destination $assembly
    $sourceHash=(Get-FileHash -LiteralPath $MetadataAssemblyPath).Hash
    $member='ResolveExecutable'
    $metadata=@(Get-WorkflowManagedMethodMetadata $assembly @($member))
    if($metadata.Count -ne 1 -or -not $metadata[0].private -or -not $metadata[0].static -or $metadata[0].ilBodyBytes -le 0) {
        throw 'Frozen baseline method does not match this actual PE-reader control.'
    }
    $checked.Add('actual frozen baseline PE metadata reports a private static method with nonempty IL without assembly loading')
    $entry=[pscustomobject]@{
        sourceInputs=@([pscustomobject]@{path='Input.psm1'})
        qualification=[pscustomobject]@{
            requiredCompiledOwnedFunctions=@([pscustomobject]@{relativePath='Input.psm1';name='Get-Parent';startLine=1})
            requiredCompiledNestedFunctions=@([pscustomobject]@{relativePath='Input.psm1';parentName='Get-Parent';parentStartLine=1
                sourceBodyName='Read-Nested';startOffset=10;endOffset=20})
        }
    }
    $manifest=[pscustomobject]@{
        files=@([pscustomobject]@{path=$assembly;role='TypedAssembly';sha256=$sourceHash;sizeBytes=(Get-Item -LiteralPath $assembly).Length})
        unitDispositionLedger=[pscustomobject]@{entries=@([pscustomobject]@{
            relativePath='Input.psm1';name='Get-Parent';startLine=1;unitId='parent-1';emittedClrMethod=$true
            retainedHostedSource=$false;omitted=$false;rejected=$false;shapingFallback=$false
            regionGraph=[pscustomobject]@{scriptBlocks=@([pscustomobject]@{startOffset=1;endOffset=30;generatedMemberName='unselected-wrapper'
                graph=[pscustomobject]@{scriptBlocks=@([pscustomobject]@{startOffset=10;endOffset=20;generatedMemberName=$member;graph=$null})}})}
        })}
    }
    $closure=@(Assert-WorkflowNestedEmission $entry $manifest $outputRoot)
    if($closure.Count -ne 1 -or $closure[0].generatedMemberName -cne $member -or $closure[0].assemblySha256 -ne $sourceHash) {
        throw 'Actual metadata closure lost the exact mapping or authenticated assembly identity.'
    }
    $checked.Add('recursive exact nested mapping binds one actual authenticated PE method; synthetic map is a harness control, not real nested workload proof')
    $bad=Copy-ContractValue $entry;$bad.qualification.requiredCompiledNestedFunctions[0].endOffset=21
    Assert-RejectedContract 'missing exact nested extent' {Assert-WorkflowNestedEmission $bad $manifest $outputRoot} 'mapping is missing or ambiguous'
    $bad=Copy-ContractValue $manifest
    $block=$bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks[0]
    $bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks=@($block,$block)
    Assert-RejectedContract 'ambiguous exact nested extent' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'mapping is missing or ambiguous'
    $bad=Copy-ContractValue $manifest;$bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks[0].generatedMemberName='DefinitelyAbsentCompilerMethod'
    Assert-RejectedContract 'nested method absent from actual PE' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'missing or ambiguous in artifact metadata'
    $bad=Copy-ContractValue $manifest;$bad.files[0].path=$MetadataAssemblyPath
    Assert-RejectedContract 'original or external DLL cannot satisfy generated artifact proof' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'escapes its declared root'
    $bad=Copy-ContractValue $manifest;$bad.files[0].sha256='a'*64
    Assert-RejectedContract 'nested assembly hash mismatch' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'differ from authenticated'
    $bad=Copy-ContractValue $manifest;$bad.files=@($bad.files[0],$bad.files[0])
    Assert-RejectedContract 'ambiguous generated typed assembly' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'exactly one authenticated'
    $bad=Copy-ContractValue $manifest;$bad.unitDispositionLedger.entries[0].retainedHostedSource=$true
    Assert-RejectedContract 'nested parent retained authored source' {Assert-WorkflowNestedEmission $entry $bad $outputRoot} 'lacks a complete emitted'
    $bad=Copy-ContractValue $entry;$bad.qualification.requiredCompiledNestedFunctions+=@($bad.qualification.requiredCompiledNestedFunctions[0])
    Assert-RejectedContract 'duplicate required authored nested extent' {Assert-WorkflowNestedContract $bad} 'extent is duplicated'
    $bad=Copy-ContractValue $entry;$bad.qualification.requiredCompiledNestedFunctions[0].parentStartLine=2
    Assert-RejectedContract 'nested parent outside declared root ownership' {Assert-WorkflowNestedContract $bad} 'one declared complete'
    $bad=Copy-ContractValue $entry;$bad.qualification.requiredCompiledNestedFunctions[0].startOffset='10'
    Assert-RejectedContract 'nested extent must be typed integer' {Assert-WorkflowNestedContract $bad} 'must be integers'
    # A real non-private method in the same frozen binary must not satisfy nested callback ownership.
    $public='ComputeUnitId'
    $bad=Copy-ContractValue $manifest;$bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks[0].generatedMemberName=$public
    Assert-RejectedContract 'actual non-private method is not a private nested callback' {
        Assert-WorkflowNestedEmission $entry $bad $outputRoot
    } 'must be private static'
    $bad=Copy-ContractValue $manifest;$bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks[0].generatedMemberName='Run'
    Assert-RejectedContract 'actual overloaded method name is ambiguous in PE metadata' {
        Assert-WorkflowNestedEmission $entry $bad $outputRoot
    } 'missing or ambiguous in artifact metadata'
    $bad=Copy-ContractValue $manifest;$bad.unitDispositionLedger.entries[0].regionGraph.scriptBlocks[0].graph.scriptBlocks[0].generatedMemberName='ResumeThread'
    Assert-RejectedContract 'actual private static native import without IL is not an emitted nested body' {
        Assert-WorkflowNestedEmission $entry $bad $outputRoot
    } 'nonempty IL body'
    $result=[ordered]@{schemaVersion=1;passed=$true;checks=@($checked);checkCount=$checked.Count
        metadataAssemblyPath=[IO.Path]::GetFullPath($MetadataAssemblyPath);metadataAssemblySha256=$sourceHash;actualPrivateMethod=$metadata[0]
        scope='Actual existing PE metadata with synthetic extent/receipt contract controls; no SDK/build, assembly load, or claim of actual platyPS nested emission'
        retainedProofRoot=$proofRoot}
    if($EvidencePath) {Write-Utf8Json -Path $EvidencePath -Value $result}
    $result|ConvertTo-Json -Depth 8
    return
}

if ($Scope -in @('All', 'OfflineMode')) {
    Assert-WholeWorkflowRestoreMode
    $checked.Add('fresh whole-workflow normal restore mode remains supported')
    $offlineRoot = Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) ('offline-' + [guid]::NewGuid().ToString('N'))
    $workflowPacketPath = Join-Path $PSScriptRoot 'Workflows/Yaml/yaml-whole-module.net10.0.json'
    $workflowPacket = Get-Content -LiteralPath $workflowPacketPath -Raw | ConvertFrom-Json
    $script:offlineCliInvocations = 0
    $savedCliOperation = (Get-Item -LiteralPath Function:\Invoke-WorkflowCliOperation).ScriptBlock
    try {
        function Invoke-WorkflowCliOperation { $script:offlineCliInvocations++; throw 'Unexpected Offline CLI invocation' }
        Assert-RejectedContract 'direct whole-workflow Offline rejects before source, observer or CLI work' {
            Invoke-WholeWorkflowQualification $workflowPacket.workloads[0] $workflowPacket $workflowPacketPath `
                (Join-Path $offlineRoot 'unavailable-source') (Join-Path $offlineRoot 'runs') -Offline
        } 'each run creates a fresh project'
    } finally { Set-Item -LiteralPath Function:\Invoke-WorkflowCliOperation -Value $savedCliOperation }
    if ($script:offlineCliInvocations -ne 0 -or (Test-Path -LiteralPath $offlineRoot)) { throw 'Offline refusal performed CLI or workspace work.' }
    $missingCli = Join-Path $offlineRoot 'unavailable-cli.dll'
    Assert-RejectedContract 'public whole-workflow Offline rejects before CLI bootstrap or workspace creation' {
        & (Join-Path $PSScriptRoot 'Invoke-ExternalQualification.ps1') -PacketPath $workflowPacketPath `
            -CliAssemblyPath $missingCli -WorkspacePath $offlineRoot -Offline -AllowExternalExecution
    } 'For an existing retained project, run normal project restore first'
    if (Test-Path -LiteralPath $offlineRoot) { throw 'Public Offline refusal created a workspace.' }
    Assert-RejectedContract 'command-only qualification preserves Offline admission' {
        & (Join-Path $PSScriptRoot 'Invoke-ExternalQualification.ps1') -PacketPath (Join-Path $PSScriptRoot 'external-assessment.net10.json') `
            -CliAssemblyPath $missingCli -WorkspacePath $offlineRoot -Offline -AllowExternalExecution
    } 'Build the PowerForge CLI first'
    if ($Scope -eq 'OfflineMode') {
        $result=[ordered]@{schemaVersion=1;passed=$true;checks=@($checked);checkCount=$checked.Count;scope='Offline mode admission and early refusal only; no CLI, SDK, source execution, acquisition or network';cliInvocations=$script:offlineCliInvocations}
        if($EvidencePath) {Write-Utf8Json -Path $EvidencePath -Value $result}
        $result|ConvertTo-Json -Depth 8
        return
    }
}

if ($Scope -in @('All', 'DurableEvidence')) {
    $proofRoot = Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) ('durable-' + [guid]::NewGuid().ToString('N'))
    $runRoot = Join-Path $proofRoot 'runs/owned'
    $durable = Join-Path $proofRoot 'evidence'
    New-Item -ItemType Directory -Path $runRoot,$durable | Out-Null
    Initialize-WorkflowEvidenceRoot $durable $runRoot | Out-Null
    $checked.Add('existing empty durable evidence directory accepted')
    Assert-RejectedContract 'durable evidence inside disposable run' {
        Initialize-WorkflowEvidenceRoot (Join-Path $runRoot 'evidence') $runRoot
    } 'must not overlap'
    Assert-RejectedContract 'durable evidence contains disposable run' {
        Initialize-WorkflowEvidenceRoot $proofRoot $runRoot
    } 'must not overlap'
    Assert-RejectedContract 'durable evidence equals disposable run' {
        Initialize-WorkflowEvidenceRoot $runRoot $runRoot
    } 'must not overlap'
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
        $linked=Join-Path $proofRoot 'linked-evidence'
        New-Item -ItemType Junction -Path $linked -Target $durable | Out-Null
        try {
            Assert-RejectedContract 'durable evidence through junction ancestor' {
                Initialize-WorkflowEvidenceRoot (Join-Path $linked 'child') $runRoot
            } 'link or junction'
        } finally {Remove-Item -LiteralPath $linked -ErrorAction Stop}
    }
    $source = Join-Path $proofRoot 'inputs'
    New-Item -ItemType Directory -Path $source | Out-Null
    [IO.File]::WriteAllText((Join-Path $source 'Input.ps1'), '# pinned input, never executed')
    [IO.File]::WriteAllText((Join-Path $source 'observer.ps1'), '# reviewed observer boundary, never executed')
    [IO.File]::WriteAllText((Join-Path $source 'fixture.ps1'), '# reviewed fixture boundary, never executed')
    $entry = [pscustomobject]@{
        id='durable-contract';entryPoint='Input.ps1'
        sourceInputs=@([pscustomobject]@{path='Input.ps1';sha256=(Get-FileHash -LiteralPath (Join-Path $source 'Input.ps1')).Hash})
        qualification=[pscustomobject]@{
            kind='HybridModuleWorkflow';requiredExports=@('Get-Value');requiredAliases=@()
            requiredCompiledOwnedFunctions=@([pscustomobject]@{relativePath='Input.ps1';name='Get-Value'})
            requiredScenarioIds=@('value');publicExportScenarios=[pscustomobject]@{'Get-Value'=@('value')}
            observerScript='observer.ps1';observerSha256=(Get-FileHash -LiteralPath (Join-Path $source 'observer.ps1')).Hash
            fixtureScript='fixture.ps1';fixtureSha256=(Get-FileHash -LiteralPath (Join-Path $source 'fixture.ps1')).Hash
        }
    }
    $packet=[pscustomobject]@{targetFramework='net10.0';semanticProfile='unused'}
    $script:durableFailures=$false
    $savedObserver=${function:Invoke-WorkflowObserver};$savedFixture=${function:Invoke-WorkflowFixture}
    $savedCli=Get-Item -LiteralPath Function:\Invoke-CliJson -ErrorAction SilentlyContinue
    try {
        function Invoke-CliJson {
            param($Arguments,$WorkingDirectory)
            $operation=$Arguments[2]
            if ($script:durableFailures -and $operation -eq 'build') {throw 'Synthetic canonical build rejection'}
            if ($operation -eq 'init') {
                Write-Utf8Json -Path $Arguments[5] -Value ([ordered]@{resources=@{mode='Declared'};artifacts=@(@{name='pinned';outputDirectory='output'})})
                return [pscustomobject]@{operation='init'}
            }
            $artifact=Join-Path $WorkingDirectory 'output/fixture.psd1'
            if ($operation -eq 'build') {
                New-Item -ItemType Directory -Path (Split-Path $artifact) -Force | Out-Null
                [IO.File]::WriteAllText($artifact,'synthetic artifact bytes; never imported')
                $ledger=[ordered]@{entries=@([ordered]@{relativePath='Input.ps1';name='Get-Value';unitId='owned-value';startLine=1
                    emittedClrMethod=$true;retainedHostedSource=$false;omitted=$false;rejected=$false;shapingFallback=$false
                    usesNativeFunctionBinding=$true;runtimeCommandRegions=0})}
                Write-Utf8Json -Path (Join-Path $WorkingDirectory '.powerforge/build/pinned.json') -Value (@{manifest=@{
                    artifactSha256=(Get-FileHash -LiteralPath $artifact).Hash;unitDispositionLedger=$ledger}})
                return [pscustomobject]@{targets=@([pscustomobject]@{path=$artifact})}
            }
            if ($operation -eq 'install') {
                $installed=Join-Path $WorkingDirectory 'installed/artifact'
                New-Item -ItemType Directory -Path $installed -Force | Out-Null
                Copy-Item -LiteralPath $artifact -Destination (Join-Path $installed 'fixture.psd1')
                return [pscustomobject]@{targets=@([pscustomobject]@{path=(Split-Path $installed)})}
            }
            return [pscustomobject]@{operation=$operation}
        }
        function Invoke-WorkflowFixture {
            param($Contract,$FixturePath,$Operation,$ScenarioRoot,$EvidenceRoot)
            $receipt=[pscustomobject]@{schemaVersion=1;operation=$Operation;stopped=$true;testArguments=@()
                fixtureIdentity=[pscustomobject]@{id='owned-synthetic-fixture'}}
            Write-Utf8Json -Path (Join-Path $EvidenceRoot ('fixture.'+$Operation+'.json')) -Value $receipt
            Write-Utf8Json -Path (Join-Path $EvidenceRoot ('fixture.'+$Operation+'.process.json')) -Value @{exitCode=0;mockedBoundary=$true}
            return $receipt
        }
        function Invoke-WorkflowObserver {
            param($Entry,$ObserverPath,$ImportPath,$Surface,$ScenarioRoot,$EvidenceRoot,$HostExecutable)
            if ($Surface -ne 'Original' -and (Test-Path -LiteralPath (Join-Path (Split-Path $ScenarioRoot) 'source'))) {
                throw 'Synthetic artifact observer saw staged compiler input.'
            }
            $report=[pscustomobject]@{schemaVersion=1;surface=$Surface;importPath=$ImportPath
                actualExports=@('Get-Value');actualAliases=@();scenarios=@([pscustomobject]@{
                    scenarioId='value';succeeded=$true;executedExports=@('Get-Value');observation=[ordered]@{value=7}})}
            Write-Utf8Json -Path (Join-Path $EvidenceRoot ($Surface+'.json')) -Value $report
            Write-Utf8Json -Path (Join-Path $EvidenceRoot ($Surface+'.process.json')) -Value @{exitCode=0;mockedBoundary=$true}
            return $report
        }
        $result=Invoke-WholeWorkflowQualification $entry $packet (Join-Path $source 'packet.json') $source $runRoot -EvidenceRoot (Join-Path $durable 'success')
        $receiptHash=(Get-FileHash -LiteralPath $result.buildReceiptPath).Hash
        Remove-Item -LiteralPath $runRoot -Recurse -ErrorAction Stop
        if (Test-Path -LiteralPath $runRoot) {throw 'Disposable workspace survived cleanup.'}
        if ((Get-FileHash -LiteralPath $result.buildReceiptPath).Hash -ne $receiptHash) {throw 'Durable build receipt changed during workspace cleanup.'}
        foreach ($file in @('Original.json','Built.json','Installed.json','Original.process.json','Built.process.json','Installed.process.json',
            'Built.source-absence.json','Installed.source-absence.json','init.json','analyze.json','lock.json','restore.json','build.json',
            'test.json','pack.json','install.json','fixture.Start.json','fixture.Stop.json','fixture.Start.process.json','fixture.Stop.process.json',
            'build-receipt.json','qualification-result.json')) {
            Get-Content -LiteralPath (Join-Path $result.evidenceRoot $file) -Raw | ConvertFrom-Json | Out-Null
        }
        if (-not $result.artifactPathsAreHistorical) {throw 'Disposed artifact paths were not labeled historical.'}
        $checked.Add('reports, process envelopes, source absence, all lifecycle steps, fixture receipts and exact build receipt survive workspace cleanup')
        Assert-RejectedContract 'durable evidence overwrite' {
            Initialize-WorkflowEvidenceRoot $result.evidenceRoot $runRoot
        } 'will not be overwritten'
        $script:durableFailures=$true
        $runRoot=Join-Path $proofRoot 'runs/failure'
        New-Item -ItemType Directory -Path $runRoot | Out-Null
        Assert-RejectedContract 'failed canonical build retains durable diagnostic' {
            Invoke-WholeWorkflowQualification $entry $packet (Join-Path $source 'packet.json') $source $runRoot -EvidenceRoot (Join-Path $durable 'failure')
        } 'Synthetic canonical build rejection'
        Remove-Item -LiteralPath $runRoot -Recurse -ErrorAction Stop
        foreach ($file in @('Original.json','build.json','qualification-failure.json','fixture.Stop.json','fixture.Stop.process.json')) {
            Get-Content -LiteralPath (Join-Path $durable ('failure/'+$file)) -Raw | ConvertFrom-Json | Out-Null
        }
        $checked.Add('failed build diagnostics and mandatory fixture Stop remain readable after disposable workspace cleanup')
    } finally {
        Set-Item -LiteralPath Function:\Invoke-WorkflowObserver -Value $savedObserver
        Set-Item -LiteralPath Function:\Invoke-WorkflowFixture -Value $savedFixture
        if ($savedCli) {Set-Item -LiteralPath Function:\Invoke-CliJson -Value $savedCli.ScriptBlock}
        else {Remove-Item -LiteralPath Function:\Invoke-CliJson}
    }
    if ($Scope -eq 'DurableEvidence') {
        $result=[ordered]@{schemaVersion=1;passed=$true;checks=@($checked);checkCount=$checked.Count
            scope='Pure PowerShell evidence retention proof with mocked compiler/observer/fixture process boundaries; no SDK or workload execution'
            retainedProofRoot=$proofRoot}
        if($EvidencePath) {Write-Utf8Json -Path $EvidencePath -Value $result}
        $result|ConvertTo-Json -Depth 8
        return
    }
}

$entry = @'
{"id":"contract-fixture","entryPoint":"Input.ps1","sourceInputs":[{"path":"Input.ps1","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],
 "qualification":{"kind":"HybridModuleWorkflow","requiredExports":["Convert-Value","Get-Value"],
 "requiredAliases":[{"name":"cv","definition":"Convert-Value"}],
 "requiredCompiledOwnedFunctions":[{"relativePath":"Input.ps1","name":"Convert-Value"}],
 "requiredScenarioIds":["round-trip","expected-error"],
 "publicExportScenarios":{"Convert-Value":["round-trip"],"Get-Value":["expected-error"]},
 "observerScript":"Observe-Fixture.ps1","observerSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}
'@ | ConvertFrom-Json
$contract = $entry.qualification
$path = Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) 'fixture.psd1'
$report = [pscustomobject]@{
    schemaVersion = 1; surface = 'Original'; importPath = $path
    actualExports = @('Get-Value', 'Convert-Value')
    actualAliases = @([pscustomobject]@{name='cv';definition='Convert-Value'})
    scenarios = @(
        [pscustomobject]@{scenarioId='round-trip';succeeded=$true;executedExports=@('Convert-Value');observation=[ordered]@{type='System.Int32';values=@(3,1,2);keys=@('z','a')}},
        [pscustomobject]@{scenarioId='expected-error';succeeded=$true;executedExports=@('Get-Value');observation=[ordered]@{success=@();error=@([ordered]@{type='System.Management.Automation.ErrorRecord';id='ExpectedError'})}}
    )
}
$ledger = [pscustomobject]@{entries=@([pscustomobject]@{
    relativePath='Input.ps1';name='Convert-Value';unitId='function-1';startLine=1;emittedClrMethod=$true
    retainedHostedSource=$false;omitted=$false;rejected=$false;shapingFallback=$false
    usesNativeFunctionBinding=$true;runtimeCommandRegions=1
})}
Assert-WorkflowQualificationContract $entry
Assert-WorkflowObservation $contract $report 'Original' $path
Assert-WorkflowCompiledOwnership $contract $ledger | Out-Null
$built = Copy-ContractValue $report; $built.surface='Built'
Assert-WorkflowObservation $contract $built 'Built' $path
Assert-WorkflowObservationParity $contract $report $built
$checked.Add('valid complete workflow preserves ordered values and expected-error records')
$checked.Add('native binding and command regions remain allowed and observable without retained source')

$bad = Copy-ContractValue $report; $bad.actualExports=@('Convert-Value')
Assert-RejectedContract 'missing public export' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'public exports differs'
$bad = Copy-ContractValue $report; $bad.actualAliases[0].definition='Get-Value'
Assert-RejectedContract 'wrong alias target' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'wrong command'
$bad = Copy-ContractValue $report; $bad.scenarios=@($bad.scenarios[0])
Assert-RejectedContract 'missing required scenario' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'scenario inventory differs'
$bad = Copy-ContractValue $report; $bad.scenarios=@($bad.scenarios[0],$bad.scenarios[0])
Assert-RejectedContract 'duplicate scenario identity' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'duplicate name'
$bad = Copy-ContractValue $report; $bad.scenarios[0].executedExports=@()
Assert-RejectedContract 'declared export was not executed' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'did not report executing'
$bad = Copy-ContractValue $report; $bad.scenarios[1].succeeded=$false
Assert-RejectedContract 'observer scenario failed' {Assert-WorkflowObservation $contract $bad 'Original' $path} 'failed or lacks'
$bad = Copy-ContractValue $entry; $bad.qualification.publicExportScenarios.PSObject.Properties.Remove('Get-Value')
Assert-RejectedContract 'public export omitted from scenario mapping' {Assert-WorkflowQualificationContract $bad} 'scenario mapping differs'
$bad = Copy-ContractValue $ledger; $bad.entries[0].retainedHostedSource=$true
Assert-RejectedContract 'emitted method retained authored source' {Assert-WorkflowCompiledOwnership $contract $bad} 'without retained source'
$bad = Copy-ContractValue $ledger; $bad.entries[0].emittedClrMethod=$false
Assert-RejectedContract 'required CLR body missing' {Assert-WorkflowCompiledOwnership $contract $bad} 'without retained source'
$bad = Copy-ContractValue $ledger; $bad.entries=@($bad.entries[0],$bad.entries[0])
Assert-RejectedContract 'ambiguous owned function identity' {Assert-WorkflowCompiledOwnership $contract $bad} 'missing or ambiguous'
$bad = Copy-ContractValue $built; $bad.scenarios[0].observation.values=@(1,2,3)
Assert-RejectedContract 'ordered output divergence' {Assert-WorkflowObservationParity $contract $report $bad} 'observation differs'
$bad = Copy-ContractValue $built; $bad.scenarios[1].observation.error[0].id='DifferentError'
Assert-RejectedContract 'error identity divergence' {Assert-WorkflowObservationParity $contract $report $bad} 'observation differs'
$bad = Copy-ContractValue $built
$bad.scenarios[0].observation=[ordered]@{keys=@('z','a');values=@(3,1,2);type='System.Int32'}
Assert-RejectedContract 'observable property order divergence' {Assert-WorkflowObservationParity $contract $report $bad} 'observation differs'
$bad = Copy-ContractValue $built; $bad.importPath=Join-Path $WorkspacePath 'wrong.psd1'
Assert-RejectedContract 'observer source fallback path' {Assert-WorkflowObservation $contract $bad 'Built' $path} 'actual'

$scriptEntry = Copy-ContractValue $entry
$scriptEntry.qualification.kind='HybridScriptApplication'
$scriptEntry.qualification.requiredExports=@(); $scriptEntry.qualification.requiredAliases=@()
$scriptEntry.qualification.publicExportScenarios=[pscustomobject]@{}
Assert-WorkflowQualificationContract $scriptEntry
$scriptReport=Copy-ContractValue $report; $scriptReport.actualExports=@(); $scriptReport.actualAliases=@()
foreach($scenario in $scriptReport.scenarios) {$scenario.executedExports=@()}
Assert-WorkflowObservation $scriptEntry.qualification $scriptReport 'Original' $path
$checked.Add('ordinary executable workflow has explicit empty module surfaces')

$source = Join-Path ([IO.Path]::GetFullPath($WorkspacePath)) ('source-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $source -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'Input.ps1'), '# fixture, never executed')
$pinned = Copy-ContractValue $entry
$pinned.sourceInputs[0].sha256=(Get-FileHash -LiteralPath (Join-Path $source 'Input.ps1')).Hash
Copy-WorkflowSourceInputs $pinned $source (Join-Path $source 'stage')
$checked.Add('exact pinned source bytes staged without executing source')
$bad=Copy-ContractValue $pinned; $bad.sourceInputs[0].sha256='c'*64
Assert-RejectedContract 'source bytes differ from pin' {Copy-WorkflowSourceInputs $bad $source (Join-Path $source 'bad-hash')} 'identity mismatch'
$bad=Copy-ContractValue $pinned; $bad.entryPoint='../Input.ps1'; $bad.sourceInputs[0].path='../Input.ps1'
Assert-RejectedContract 'source containment traversal' {Copy-WorkflowSourceInputs $bad $source (Join-Path $source 'bad-path')} 'portable relative'

# Exercise actual owned-directory withdrawal and finally restoration without a process.
# The only mock is the external observer boundary, which this proof must not execute.
$script:withdrawnInput=Join-Path $source 'stage'
$script:rejectObserver=$false
$savedObserver=${function:Invoke-WorkflowObserver}
try {
    function Invoke-WorkflowObserver {
        param($Entry,$ObserverPath,$ImportPath,$Surface,$ScenarioRoot,$EvidenceRoot,$HostExecutable)
        if(Test-Path -LiteralPath $script:withdrawnInput) {throw 'Compiler source remained visible.'}
        if($script:rejectObserver) {throw 'Synthetic observer rejection'}
        return [pscustomobject]@{surface=$Surface}
    }
    Invoke-WorkflowArtifactObserver $pinned 'unused' 'unused' 'Built' $WorkspacePath $WorkspacePath 'unused' $source $script:withdrawnInput | Out-Null
    if(-not(Test-Path -LiteralPath $script:withdrawnInput)) {throw 'Source was not restored after observation.'}
    $checked.Add('compiler input absent during artifact observation and restored afterward')
    $script:rejectObserver=$true
    Assert-RejectedContract 'observer failure still restores source' {
        Invoke-WorkflowArtifactObserver $pinned 'unused' 'unused' 'Installed' $WorkspacePath $WorkspacePath 'unused' $source $script:withdrawnInput
    } 'Synthetic observer rejection'
    Assert-WorkflowSourceIdentities $pinned $script:withdrawnInput
    if(Test-Path -LiteralPath ($script:withdrawnInput+'.withdrawn')) {throw 'Source holding directory leaked.'}
} finally { Set-Item -LiteralPath Function:\Invoke-WorkflowObserver -Value $savedObserver }

Assert-WorkflowTestInvocation ([pscustomobject]@{testArguments=@('', 'two words', '--literal', 'żółć');testWorkingDirectory='scenarios'}) -RequireArguments
$checked.Add('fixture argv preserves literal strings and project-relative scenario cwd')
Assert-RejectedContract 'non-string fixture argv' {Assert-WorkflowTestInvocation ([pscustomobject]@{testArguments=@(2)}) -RequireArguments} 'only strings'
Assert-RejectedContract 'NUL in fixture argv' {Assert-WorkflowTestInvocation ([pscustomobject]@{testArguments=@("a$([char]0)b")}) -RequireArguments} 'without NUL'
Assert-RejectedContract 'fixture cwd escapes project' {Assert-WorkflowTestInvocation ([pscustomobject]@{testArguments=@();testWorkingDirectory='../outside'}) -RequireArguments} 'project-relative'

$fixtureEntry=Copy-ContractValue $scriptEntry
$fixtureEntry.sourceInputs=$pinned.sourceInputs
$fixtureEntry.qualification|Add-Member -NotePropertyName fixtureScript -NotePropertyValue 'fixture.ps1'
$fixtureEntry.qualification|Add-Member -NotePropertyName fixtureSha256 -NotePropertyValue ('d'*64)
$bad=Copy-ContractValue $fixtureEntry
$bad.qualification|Add-Member -NotePropertyName testArguments -NotePropertyValue @()
Assert-RejectedContract 'fixture and static invocation conflict' {Assert-WorkflowQualificationContract $bad} 'contradictory static'

# Keep live/process boundaries mocked, but exercise the real common lifecycle's finally.
# The original observer rejects before init, so no compiler or SDK path is reachable.
$fixtureScript=Join-Path $source 'fixture.ps1'; [IO.File]::WriteAllText($fixtureScript,'# reviewed fixture boundary, not executed')
$observerScript=Join-Path $source 'Observe-Fixture.ps1'; [IO.File]::WriteAllText($observerScript,'# reviewed observer boundary, not executed')
$fixtureEntry.qualification.fixtureSha256=(Get-FileHash -LiteralPath $fixtureScript).Hash
$fixtureEntry.qualification.observerSha256=(Get-FileHash -LiteralPath $observerScript).Hash
$script:fixtureCalls=[Collections.Generic.List[string]]::new(); $script:failFixtureStop=$false
$savedFixture=${function:Invoke-WorkflowFixture}; $savedObserver=${function:Invoke-WorkflowObserver}
try {
    function Invoke-WorkflowFixture {
        param($Contract,$FixturePath,$Operation,$ScenarioRoot,$EvidenceRoot)
        $script:fixtureCalls.Add($Operation)
        if($Operation -eq 'Stop' -and $script:failFixtureStop) {throw 'Synthetic fixture Stop failure'}
        return [pscustomobject]@{schemaVersion=1;operation=$Operation;stopped=$true;testArguments=@();testWorkingDirectory='scenarios';fixtureIdentity=[pscustomobject]@{id='fixture-boundary'}}
    }
    function Invoke-WorkflowObserver {throw 'Synthetic original observer rejection'}
    $packet=[pscustomobject]@{targetFramework='net10.0';semanticProfile='unused'}
    $runRoot=Join-Path $WorkspacePath ('lifecycle-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Assert-RejectedContract 'fixture stops after original observer failure' {
        Invoke-WholeWorkflowQualification $fixtureEntry $packet (Join-Path $source 'packet.json') $source $runRoot
    } 'Synthetic original observer rejection'
    if(($script:fixtureCalls -join ',') -cne 'Start,Stop') {throw 'Fixture cleanup did not run exactly once in finally.'}
    $script:failFixtureStop=$true
    $runRoot=Join-Path $WorkspacePath ('lifecycle-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Assert-RejectedContract 'fixture Stop failure blocks qualification' {
        Invoke-WholeWorkflowQualification $fixtureEntry $packet (Join-Path $source 'packet.json') $source $runRoot
    } 'Synthetic fixture Stop failure'
} finally {
    Set-Item -LiteralPath Function:\Invoke-WorkflowObserver -Value $savedObserver
    Set-Item -LiteralPath Function:\Invoke-WorkflowFixture -Value $savedFixture
}

$result=[ordered]@{schemaVersion=1;passed=$true;checks=@($checked);checkCount=$checked.Count
    scope='Standalone qualification assertions only; no compiler, SDK, third-party source, lifecycle process, or benchmark executed'
    retainedFixtureRoot=$source}
if($EvidencePath) {Write-Utf8Json -Path $EvidencePath -Value $result}
$result|ConvertTo-Json -Depth 8
