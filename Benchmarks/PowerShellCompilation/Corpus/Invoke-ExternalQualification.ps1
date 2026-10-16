[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'external-assessment.net10.json'),
    [string] $CliAssemblyPath,
    [string] $SourceRoot,
    [string] $WorkspacePath,
    [string] $EvidencePath,
    [string] $DurableEvidencePath,
    [string[]] $WorkloadId,
    [switch] $Offline,
    [switch] $AllowExternalExecution
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Qualification.Workflow.ps1')

if (-not $AllowExternalExecution) {
    throw 'External qualification imports and invokes reviewed third-party source. Pass -AllowExternalExecution to authorize this local execution lane.'
}

function Invoke-CliJson {
    param([string[]] $Arguments, [string] $WorkingDirectory, [string] $ProcessEvidencePath)

    $run = Invoke-OwnedProcess -FileName 'dotnet' -Arguments (@($CliAssemblyPath) + $Arguments) -WorkingDirectory $WorkingDirectory
    $json = ConvertFrom-CorpusCliProcess $run -ProcessEvidencePath $ProcessEvidencePath
    return $json.result
}

function Quote-PowerShellLiteral {
    param([string] $Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

function New-QualificationInvocation {
    param(
        [string] $ImportPath,
        [string] $ImportSurface,
        [string] $ProbeScript,
        [string] $SetupScript
    )

    $load = if ($ImportSurface -eq 'DotSource') {
        '. ' + (Quote-PowerShellLiteral $ImportPath)
    } else {
        'Import-Module -Name ' + (Quote-PowerShellLiteral $ImportPath) + ' -Force -ErrorAction Stop'
    }
    $segments = @($SetupScript, $load, $ProbeScript) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    return $segments -join '; '
}

$PacketPath = [IO.Path]::GetFullPath($PacketPath)
$packet = Get-Content -LiteralPath $PacketPath -Raw | ConvertFrom-Json
if ($packet.schemaVersion -ne 1) { throw "Unsupported external-assessment schema $($packet.schemaVersion)." }
$qualified = @($packet.workloads | Where-Object { $null -ne $_.PSObject.Properties['qualification'] })
if ($WorkloadId) { $qualified = @($qualified | Where-Object { $_.id -in $WorkloadId }) }
$unknown = @($WorkloadId | Where-Object { $_ -notin @($packet.workloads.id) } | Sort-Object -Unique)
if ($unknown.Count -gt 0) { throw "Unknown assessment workload id(s): $($unknown -join ', ')" }
if ($qualified.Count -eq 0) { throw 'No selected workload declares an external qualification contract.' }
Assert-WorkflowNameSet @($qualified.id) @($qualified.id) 'Selected workload IDs'
$selectedCommands = @($qualified | Where-Object { $_.qualification.kind -eq 'HybridModuleCommand' })
$workflowEntries = @($qualified | Where-Object { $_.qualification.kind -in @('HybridModuleWorkflow', 'HybridScriptApplication') })
if ($workflowEntries.Count -gt 0) { Assert-WholeWorkflowRestoreMode -Offline:$Offline }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $CliAssemblyPath) { $CliAssemblyPath = Join-Path $repositoryRoot 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
$CliAssemblyPath = [IO.Path]::GetFullPath($CliAssemblyPath)
if (-not (Test-Path -LiteralPath $CliAssemblyPath -PathType Leaf)) { throw "Build the PowerForge CLI first: $CliAssemblyPath" }
if (-not $WorkspacePath) { $WorkspacePath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/ExternalQualification' }
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
New-Item -ItemType Directory -Path $WorkspacePath -Force | Out-Null
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $WorkspacePath ('runs/' + $runId)
Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Qualification run' | Out-Null
if (-not $EvidencePath) { $EvidencePath = Join-Path $WorkspacePath ('evidence/' + $runId + '.json') }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
Assert-WorkflowPathWithoutLinks $EvidencePath
if (Test-Path -LiteralPath $EvidencePath) { throw 'Qualification summary already exists; existing evidence will not be overwritten.' }
if ($EvidencePath.Equals($runRoot, (Get-PathComparison)) -or
    $EvidencePath.StartsWith($runRoot + [IO.Path]::DirectorySeparatorChar, (Get-PathComparison))) {
    throw 'Qualification summary must be outside the disposable run root.'
}
if ($workflowEntries.Count -gt 0) {
    if (-not $DurableEvidencePath) {
        $DurableEvidencePath = Join-Path (Split-Path $EvidencePath) ([IO.Path]::GetFileNameWithoutExtension($EvidencePath) + '.workflows-' + $runId)
    }
    $DurableEvidencePath = Initialize-WorkflowEvidenceRoot $DurableEvidencePath $runRoot
}

try {
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    if ($selectedCommands.Count -gt 0) {
        $assessmentEvidence = Join-Path $runRoot 'assessment.json'
        $assessmentArguments = @{
            PacketPath = $PacketPath
            CliAssemblyPath = $CliAssemblyPath
            WorkspacePath = $WorkspacePath
            EvidencePath = $assessmentEvidence
            WorkloadId = @($selectedCommands.id)
            Offline = $Offline
        }
        & (Join-Path $PSScriptRoot 'Invoke-ExternalAssessment.ps1') @assessmentArguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "The non-executing assessment prerequisite exited with $LASTEXITCODE." }
    }

    $results = [Collections.Generic.List[object]]::new()
    foreach ($entry in $qualified) {
        if ($entry.qualification.kind -in @('HybridModuleWorkflow', 'HybridScriptApplication')) {
            Write-Information "[$($entry.id)] build, test, pack, install, and observe reviewed whole artifact workflow" -InformationAction Continue
        } else {
            Write-Information "[$($entry.id)] build, import, and invoke reviewed Hybrid command" -InformationAction Continue
        }
        $entryEvidenceRoot = $null
        try {
            if ($entry.qualification.kind -in @('HybridModuleWorkflow', 'HybridScriptApplication')) {
                if ($entry.id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw 'Workflow id must be a portable filename component.' }
                $entryEvidenceRoot = Assert-ContainedPath -Root $DurableEvidencePath -Path (Join-Path $DurableEvidencePath $entry.id) -Label 'Durable workflow evidence'
                $results.Add((Invoke-WholeWorkflowQualification -Entry $entry -Packet $packet -PacketPath $PacketPath `
                    -SourceRoot $SourceRoot -RunRoot $runRoot -EvidenceRoot $entryEvidenceRoot -Offline:$Offline))
                continue
            }
            if ($entry.qualification.kind -ne 'HybridModuleCommand') { throw "Unsupported qualification kind '$($entry.qualification.kind)'." }
            if ([string]::IsNullOrWhiteSpace($entry.qualification.unitName) -or [string]::IsNullOrWhiteSpace($entry.qualification.probeScript)) {
                throw 'HybridModuleCommand qualification requires unitName and probeScript.'
            }
            $importSurface = if ($null -ne $entry.qualification.PSObject.Properties['importSurface']) {
                [string] $entry.qualification.importSurface
            } else {
                'Manifest'
            }
            $originalImportSurface = if ($null -ne $entry.qualification.PSObject.Properties['originalImportSurface']) {
                [string] $entry.qualification.originalImportSurface
            } else {
                $importSurface
            }
            $compiledImportSurface = if ($null -ne $entry.qualification.PSObject.Properties['compiledImportSurface']) {
                [string] $entry.qualification.compiledImportSurface
            } else {
                $importSurface
            }
            $supportedSurfaces = @('Manifest', 'RootModuleDirect', 'DotSource', 'TypedAssembly')
            if ($originalImportSurface -notin $supportedSurfaces -or $compiledImportSurface -notin $supportedSurfaces) {
                throw "Unsupported qualification import surfaces '$originalImportSurface' and '$compiledImportSurface'."
            }
            if ($originalImportSurface -eq 'TypedAssembly') {
                throw 'TypedAssembly is valid only for the generated artifact surface.'
            }
            $setupScript = if ($null -ne $entry.qualification.PSObject.Properties['setupScript']) {
                [string] $entry.qualification.setupScript
            } else {
                ''
            }
            $selectedSourceRoot = Join-Path $WorkspacePath ('extract/' + $entry.acquisition.sha256)
            $qualificationEntryPoint = if ($null -ne $entry.qualification.PSObject.Properties['entryPoint']) {
                [string] $entry.qualification.entryPoint
            } else {
                [string] $entry.entryPoint
            }
            $sourcePath = Assert-ContainedPath -Root $selectedSourceRoot -Path (Join-Path $selectedSourceRoot $qualificationEntryPoint) -Label 'Qualification entry point'
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Qualification entry point does not exist: $sourcePath" }
            $analysis = Invoke-CliJson -Arguments @('powershell', 'analyze', $sourcePath, '--kind', 'dll', '--mode', 'Hybrid', '--framework', $packet.targetFramework, '--semantic-profile', $packet.semanticProfile, '--resource-mode', 'Declared', '--output', 'json') -WorkingDirectory $runRoot
            $lockPath = Join-Path $runRoot ($entry.id + '.lock.json')
            Write-Utf8Json -Path $lockPath -Value $analysis.dependencyGraph
            $artifactName = 'PowerForgeQualification' + (($entry.id -split '[^A-Za-z0-9]+' | Where-Object { $_.Length -gt 0 }) -join '')
            $build = Invoke-CliJson -Arguments @('powershell', 'build', $sourcePath, '--kind', 'dll', '--mode', 'Hybrid', '--framework', $packet.targetFramework, '--semantic-profile', $packet.semanticProfile, '--out', (Join-Path $runRoot 'output'), '--name', $artifactName, '--resource-mode', 'Declared', '--dependency-lock', $lockPath, '--no-build-cache', '--output', 'json') -WorkingDirectory $runRoot
            $ledgerEntry = @($build.manifest.unitDispositionLedger.entries | Where-Object { $_.name -eq $entry.qualification.unitName })
            if ($ledgerEntry.Count -ne 1 -or $ledgerEntry[0].emittedClrMethod -ne $true) {
                throw "Qualification unit '$($entry.qualification.unitName)' was not emitted as exactly one CLR method."
            }

            $compiledImportPath = [string] $build.artifactPath
            if ($compiledImportSurface -eq 'RootModuleDirect') {
                $artifactRoot = Split-Path -Parent $compiledImportPath
                $compiledManifest = Import-PowerShellDataFile -LiteralPath $compiledImportPath
                if ([string]::IsNullOrWhiteSpace($compiledManifest.RootModule)) { throw 'Generated module manifest declares no root module.' }
                $compiledImportPath = Assert-ContainedPath -Root $artifactRoot -Path (Join-Path $artifactRoot $compiledManifest.RootModule) -Label 'Qualification generated root module'
                if (-not (Test-Path -LiteralPath $compiledImportPath -PathType Leaf)) { throw "Generated root module does not exist: $compiledImportPath" }
            } elseif ($compiledImportSurface -eq 'TypedAssembly') {
                $typedAssemblies = @($build.manifest.files | Where-Object role -eq 'TypedAssembly')
                if ($typedAssemblies.Count -ne 1) { throw 'Generated qualification artifact does not contain exactly one typed assembly.' }
                $artifactRoot = Split-Path -Parent ([string] $build.artifactPath)
                $compiledImportPath = Assert-ContainedPath -Root $artifactRoot -Path ([string] $typedAssemblies[0].path) -Label 'Qualification generated typed assembly'
                if (-not (Test-Path -LiteralPath $compiledImportPath -PathType Leaf)) { throw "Generated typed assembly does not exist: $compiledImportPath" }
            }
            $originalCommand = New-QualificationInvocation -ImportPath $sourcePath -ImportSurface $originalImportSurface -ProbeScript $entry.qualification.probeScript -SetupScript $setupScript
            $compiledCommand = New-QualificationInvocation -ImportPath $compiledImportPath -ImportSurface $compiledImportSurface -ProbeScript $entry.qualification.probeScript -SetupScript $setupScript
            $original = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoProfile', '-NonInteractive', '-Command', $originalCommand) -WorkingDirectory $runRoot -TimeoutSeconds 120
            $compiled = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoProfile', '-NonInteractive', '-Command', $compiledCommand) -WorkingDirectory $runRoot -TimeoutSeconds 120
            if ($original.ExitCode -ne 0 -or $compiled.ExitCode -ne 0 -or
                $original.StandardOutput -ne $compiled.StandardOutput -or $original.StandardError -ne $compiled.StandardError) {
                throw "Original and compiled qualification invocations differ. OriginalExit=$($original.ExitCode); CompiledExit=$($compiled.ExitCode)."
            }
            $results.Add([ordered]@{
                id = $entry.id
                unitName = $entry.qualification.unitName
                originalImportSurface = $originalImportSurface
                compiledImportSurface = $compiledImportSurface
                emittedClrMethod = $true
                runtimeRouted = [int] $ledgerEntry[0].runtimeCommandRegions -gt 0 -or [bool] $ledgerEntry[0].retainedHostedSource
                compiledMethods = [int] $build.manifest.compiledMethods
                outputSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($compiled.StandardOutput))).ToLowerInvariant()
                invocationParity = $true
                completeWorkloadExecution = $false
                wholeArtifactWorkflowQualified = $false
                scenarioCount = 1
                succeeded = $true
            })
        } catch {
            if ($entryEvidenceRoot -and (Test-Path -LiteralPath $entryEvidenceRoot -PathType Container)) {
                Write-Utf8Json -Path (Join-Path $entryEvidenceRoot 'qualification-failure.json') -Value ([ordered]@{
                    id=$entry.id;succeeded=$false;error=$_.Exception.Message;evidenceRoot=$entryEvidenceRoot;ownedProcess=(Get-CorpusOwnedProcessFailureEvidence $_.Exception)
                })
            }
            $results.Add([ordered]@{ id = $entry.id; succeeded = $false; emittedClrMethod = $false;
                wholeArtifactWorkflowQualified = $false; scenarioCount = 0; error = $_.Exception.Message;
                evidenceRoot = $entryEvidenceRoot; cliFailure = (Get-CorpusCliFailureEvidence $_.Exception); ownedProcess = (Get-CorpusOwnedProcessFailureEvidence $_.Exception) })
        }
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        packetId = $packet.packetId
        packetSha256 = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        semanticProfile = $packet.semanticProfile
        targetFramework = $packet.targetFramework
        externalExecutionAuthorized = $true
        qualifications = @($results)
        summary = [ordered]@{
            passed = @($results | Where-Object succeeded).Count
            total = $results.Count
            emittedCommandsInvoked = @($results | Where-Object { $_.succeeded -and $_.emittedClrMethod }).Count
            completeWorkloadExecutions = 0
            wholeArtifactWorkflowsQualified = @($results | Where-Object { $_.succeeded -and $_.wholeArtifactWorkflowQualified }).Count
            declaredWorkflowScenarios = [int] (($results | Where-Object { $_.succeeded -and $_.wholeArtifactWorkflowQualified } |
                ForEach-Object { $_.scenarioCount } | Measure-Object -Sum).Sum)
        }
        retainedWorkflowRoot = if ($workflowEntries.Count) { $DurableEvidencePath } else { $null }
        artifactPathsAreHistorical = $true
        interpretation = 'HybridModuleCommand proves parity for the named emitted command. Whole-workflow lanes additionally enforce the declared public surface, required complete CLR bodies, observed scenario/export coverage and original/built/installed parity through the canonical project lifecycle. Reviewed observer reports are evidence, not exhaustive instruction or branch instrumentation. No lane implies execution without PowerShell.'
    }
    Write-Utf8Json -Path $EvidencePath -Value $evidence
    Write-Information "Evidence: $EvidencePath" -InformationAction Continue
    if ($selectedCommands.Count -gt 0) {
        Write-Information "Qualified emitted commands: $($evidence.summary.emittedCommandsInvoked)/$($selectedCommands.Count)" -InformationAction Continue
    }
    if ($workflowEntries.Count -gt 0) {
        Write-Information "Qualified whole artifact workflows: $($evidence.summary.wholeArtifactWorkflowsQualified)/$($workflowEntries.Count); declared scenarios: $($evidence.summary.declaredWorkflowScenarios)" -InformationAction Continue
    }
    if ($evidence.summary.passed -ne $evidence.summary.total) { exit 1 }
} finally {
    if (Test-Path -LiteralPath $runRoot) {
        Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Qualification run cleanup' | Out-Null
        Assert-WorkflowPathWithoutLinks $runRoot
        if (@(Get-ChildItem -LiteralPath $runRoot -Recurse -Force | Where-Object {
            $_.Attributes -band [IO.FileAttributes]::ReparsePoint
        }).Count) { throw 'Qualification run contains a link or junction; preserve it for scoped recovery.' }
        $fixtureCleanupFailed = @($workflowEntries | Where-Object {
            Test-Path -LiteralPath (Join-Path $DurableEvidencePath ($_.id + '/fixture-cleanup-failure.json')) -PathType Leaf
        }).Count -gt 0
        if ($fixtureCleanupFailed) {
            Write-Utf8Json -Path (Join-Path $DurableEvidencePath 'cleanup-held.json') -Value ([ordered]@{
                runRoot=$runRoot;removed=$false;reason='Fixture Stop failed; owned process release must be verified before removing its workspace.'
            })
            Write-Information "Fixture cleanup failed; retained owned workspace for exact-identity recovery: $runRoot" -InformationAction Continue
        } else { Remove-Item -LiteralPath $runRoot -Recurse -ErrorAction Stop }
    }
}
