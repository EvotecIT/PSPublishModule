#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $EvidencePath,

    [Parameter(Mandatory)]
    [string] $OutputPath,

    [string] $PacketPath = (Join-Path $PSScriptRoot 'unfamiliar-workflows.net10.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Assessment.Acquisition.ps1')
. (Join-Path $PSScriptRoot 'Corpus.WorkflowBaseline.ps1')

function Get-ObservationHash {
    param([AllowEmptyString()][string] $Text)

    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

function ConvertTo-PublishedScenario {
    param($Case)

    return [ordered]@{
        id = $Case.id
        exitCode = $Case.exitCode
        stdoutSha256 = Get-ObservationHash $Case.stdout
        stderrSha256 = Get-ObservationHash $Case.stderr
        changedFiles = $Case.changedFiles
        removedFiles = @($Case.removedFiles)
    }
}

function Assert-PublishedCaseInventory {
    param($Cases, [string] $Lane, [string] $Surface)

    if ($Cases.Count -ne 2 -or $Cases[0].id -cne 'success' -or $Cases[1].id -cne 'alternate') {
        throw "$Lane does not contain both declared $Surface cases."
    }
}

$packet = Read-AssessmentPacket $PacketPath
$output = [IO.Path]::GetFullPath($OutputPath)
$fixtureNames = @('Unfamiliar.Fixtures.ps1', 'Observe-UnfamiliarModule.ps1')
$fixtureSets = @{}
$replayedSourceLanes = [Collections.Generic.List[object]]::new()

$expected = [ordered]@{}
foreach ($entry in $packet.Packet.workloads) {
    Assert-AssessmentWorkloadId $entry.id
    foreach ($target in $entry.artifactTargets) {
        $key = "$($entry.id)/$target"
        if ($expected.Contains($key)) { throw "The packet declares '$key' more than once." }
        $expected[$key] = Get-CorpusProperty $packet.Packet.artifactMatrix $target
        if ($null -eq $expected[$key]) { throw "No host contract exists for '$key'." }
    }
}

$rawByLane = @{}
$compiler = $null
$hosts = [ordered]@{}
foreach ($path in $EvidencePath) {
    $evidenceFile = (Resolve-Path -LiteralPath $path).Path
    if ($evidenceFile -eq $output) { throw 'The published baseline cannot overwrite its input evidence.' }
    $report = Get-Content -LiteralPath $evidenceFile -Raw | ConvertFrom-Json -AsHashtable
    if ($report.schemaVersion -ne 2 -or $report.packetId -cne $packet.Packet.packetId -or $report.packetSha256 -cne $packet.Sha256) {
        throw "Evidence '$path' does not identify the selected packet."
    }
    $fixtureHashes = [ordered]@{}
    foreach ($name in $fixtureNames) {
        $hash = $report.fixtures[$name]
        if ($hash -cnotmatch '^[0-9a-f]{64}$') { throw "Evidence '$path' has no exact hash for fixture '$name'." }
        $fixtureHashes[$name] = $hash
    }
    $fixtureSet = Get-ObservationHash (ConvertTo-Json -InputObject $fixtureHashes -Compress)
    $fixtureSets[$fixtureSet] = $fixtureHashes
    if ([string]::IsNullOrWhiteSpace($report.compilerVersion)) { throw "Evidence '$path' has no compiler identity." }
    if ($null -eq $compiler) { $compiler = $report.compilerVersion }
    if ($report.compilerVersion -cne $compiler) { throw 'A baseline requires one compiler revision across its target lanes.' }
    foreach ($lane in $report.lanes) {
        $key = "$($lane.id)/$($lane.target)"
        if (-not $expected.Contains($key)) { throw "Evidence '$path' reports an undeclared lane '$key'." }
        $reference = $report.hosts[$lane.target]
        Assert-WorkflowReferenceHost $reference $expected[$key] $key
        $hosts[$lane.target] = $reference
        if ($rawByLane.ContainsKey($key)) {
            $previous = $rawByLane[$key]
            if ($previous.lane.stage -notin @('acquire', 'source') -or
                $lane.stage -in @('acquire', 'source') -or $previous.fixtureSet -ceq $fixtureSet) {
                throw "More than one observation was supplied for '$key'. Only an invalid source observation can be replayed with corrected fixtures."
            }
            $replayedSourceLanes.Add([ordered]@{
                id = $lane.id
                target = $lane.target
                originalStage = $previous.lane.stage
                originalFixtureSet = $previous.fixtureSet
            })
        }
        $rawByLane[$key] = @{lane = $lane; fixtureSet = $fixtureSet}
    }
}

$missing = @($expected.Keys | Where-Object { -not $rawByLane.ContainsKey($_) })
if ($missing.Count) { throw "The baseline is missing $($missing.Count) declared lanes. $($missing -join ', ')" }

$lanes = foreach ($key in $expected.Keys) {
    $record = $rawByLane[$key]
    $lane = $record.lane
    $surfaceRecords = [ordered]@{}
    Assert-PublishedCaseInventory @($lane.observations.source) $key 'source'
    if ($lane.observations.source[0].exitCode -ne 0) {
        throw "The source success case for '$key' failed. Resolve the fixture or prerequisite before publishing the baseline."
    }
    if ($lane.status -in @('parity', 'semantic-difference')) {
        Assert-PublishedCaseInventory @($lane.observations.built) $key 'built'
        Assert-PublishedCaseInventory @($lane.observations.installed) $key 'installed'
        if ($lane.stage -cne 'complete' -or $lane.artifactSha256 -cnotmatch '^[0-9a-f]{64}$') {
            throw "Completed lane '$key' has no authenticated artifact identity."
        }
        $original = ConvertTo-Json -InputObject $lane.observations.source -Depth 15 -Compress
        $built = ConvertTo-Json -InputObject $lane.observations.built -Depth 15 -Compress
        $installed = ConvertTo-Json -InputObject $lane.observations.installed -Depth 15 -Compress
        if (($lane.status -ceq 'parity') -ne ($original -ceq $built -and $original -ceq $installed)) {
            throw "Reported parity for '$key' disagrees with the recorded observations."
        }
    } elseif ($lane.status -cne 'stage-failed' -or $lane.stage -in @('acquire', 'source')) {
        throw "Lane '$key' did not reach a recorded project outcome."
    }

    foreach ($surface in 'source', 'built', 'installed') {
        if (-not $lane.observations.Contains($surface)) { continue }
        $surfaceRecords[$surface] = @($lane.observations[$surface] | ForEach-Object { ConvertTo-PublishedScenario $_ })
    }
    [ordered]@{
        id = $lane.id
        target = $lane.target
        status = $lane.status
        stage = $lane.stage
        artifactSha256 = Get-CorpusProperty $lane 'artifactSha256'
        emittedBodies = @(Get-CorpusProperty $lane 'emittedBodies' @())
        fixtureSet = $record.fixtureSet
        scenarios = $surfaceRecords
    }
}

$workflowsWithParity = @($lanes | Group-Object { $_.id } | Where-Object {
    @($_.Group | Where-Object { $_.status -cne 'parity' }).Count -eq 0
}).Count
$published = [ordered]@{
    schemaVersion = 2
    packetId = $packet.Packet.packetId
    packetSha256 = $packet.Sha256
    compilerVersion = $compiler
    fixtureSets = [ordered]@{}
    replayedSourceLanes = @($replayedSourceLanes)
    hosts = $hosts
    totals = [ordered]@{
        workflows = @($packet.Packet.workloads).Count
        observedLanes = @($lanes).Count
        parityLanes = @($lanes | Where-Object { $_.status -ceq 'parity' }).Count
        semanticDifferenceLanes = @($lanes | Where-Object { $_.status -ceq 'semantic-difference' }).Count
        failedStageLanes = @($lanes | Where-Object { $_.status -ceq 'stage-failed' }).Count
        workflowsWithParityOnAllDeclaredTargets = $workflowsWithParity
    }
    lanes = @($lanes)
}
foreach ($identity in ($fixtureSets.Keys | Sort-Object)) { $published.fixtureSets[$identity] = $fixtureSets[$identity] }
Write-Utf8Json $output $published
[pscustomobject] $published.totals
