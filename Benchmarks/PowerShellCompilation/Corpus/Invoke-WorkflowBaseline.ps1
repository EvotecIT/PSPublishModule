#Requires -Version 7.4
[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'unfamiliar-workflows.net10.json'),

    [Parameter(Mandatory)]
    [string] $CliAssemblyPath,

    [Parameter(Mandatory)]
    [string] $WorkspacePath,

    [Parameter(Mandatory)]
    [string] $PowerShell76Path,

    [string] $PayloadCachePath,

    [string[]] $WorkloadId,

    [string[]] $Framework,

    [switch] $OfflineAcquisition,

    [switch] $AllowExternalExecution
)

$ErrorActionPreference = 'Stop'
if (-not $AllowExternalExecution) { throw 'This runner invokes the pinned third-party workloads. Pass -AllowExternalExecution to run them locally.' }
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Assessment.Acquisition.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Qualification.Workflow.ps1')
. (Join-Path $PSScriptRoot 'Corpus.WorkflowBaseline.ps1')
. (Join-Path $PSScriptRoot 'Workflows/Unfamiliar/Unfamiliar.Fixtures.ps1')

$canonicalCli = (Resolve-Path -LiteralPath $CliAssemblyPath).Path
$CliAssemblyPath = $canonicalCli
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
New-Item -ItemType Directory -Path $WorkspacePath -Force | Out-Null
Assert-WorkflowPathWithoutLinks $WorkspacePath
$runRoot = Join-Path $WorkspacePath ('w/' + [guid]::NewGuid().ToString('N').Substring(0, 12))
New-Item -ItemType Directory -Path $runRoot | Out-Null

if (-not $PayloadCachePath) { $PayloadCachePath = Join-Path $WorkspacePath 'payloads' }
$PayloadCachePath = [IO.Path]::GetFullPath($PayloadCachePath)
$packetState = Read-AssessmentPacket $PacketPath
$packet = $packetState.Packet
$fixtureIdentities = [ordered]@{}
foreach ($name in @('Unfamiliar.Fixtures.ps1', 'Observe-UnfamiliarModule.ps1')) {
    $path = Join-Path $PSScriptRoot "Workflows/Unfamiliar/$name"
    $fixtureIdentities[$name] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}
$hostPaths = @{
    'net10.0' = (Resolve-Path -LiteralPath $PowerShell76Path).Path
    'net472' = (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe')
}
$selected = @($packet.workloads | Where-Object { -not $WorkloadId -or $_.id -in $WorkloadId })
foreach ($id in $WorkloadId) {
    if ($id -notin $selected.id) { throw "Unknown workload '$id'." }
}

$hostIdentities = [ordered]@{}
foreach ($target in ($selected.artifactTargets | Sort-Object -Unique)) {
    if ($Framework -and $target -notin $Framework) { continue }
    $profile = Get-CorpusProperty $packet.artifactMatrix $target
    if ($null -eq $profile -or -not $hostPaths.ContainsKey($target)) { throw "Missing host contract for '$target'." }
    $versionProbe = Invoke-OwnedProcess -FileName $hostPaths[$target] -Arguments @(
        '-NoLogo', '-NoProfile', '-NonInteractive', '-Command', '$PSVersionTable.PSVersion.ToString()'
    ) -WorkingDirectory $runRoot -TimeoutSeconds 30
    if ($versionProbe.ExitCode -ne 0 -or $versionProbe.StandardOutput.Trim() -cne $profile.referenceHost) {
        throw "The $target oracle must be PowerShell $($profile.referenceHost). Received '$($versionProbe.StandardOutput.Trim())'."
    }
    $hostIdentities[$target] = $versionProbe.StandardOutput.Trim()
}
if ($hostIdentities.Count -eq 0) { throw 'The selection has no declared target lanes.' }

$cliVersion = Invoke-OwnedProcess -FileName 'dotnet' -Arguments @($CliAssemblyPath, '--version') -WorkingDirectory $runRoot -TimeoutSeconds 30
if ($cliVersion.ExitCode -ne 0) { throw 'PowerForge version could not be verified.' }
$report = [ordered]@{
    schemaVersion = 1
    packetId = $packet.packetId
    packetSha256 = $packetState.Sha256
    fixtures = $fixtureIdentities
    compilerVersion = $cliVersion.StandardOutput.Trim()
    hosts = $hostIdentities
    lanes = [Collections.Generic.List[object]]::new()
}
$evidencePath = Join-Path $runRoot 'workflow-baseline.json'
Write-Information "Baseline evidence: $evidencePath" -InformationAction Continue
foreach ($entry in $selected) {
    Assert-AssessmentWorkloadId $entry.id
    $targets = @($entry.artifactTargets | Where-Object { $hostIdentities.Contains($_) })
    if ($targets.Count -eq 0) { continue }
    $payload = Get-VerifiedPayload $entry $PayloadCachePath -OfflineMode:$OfflineAcquisition
    $sourceRoot = Get-AssessmentSourceRoot -Entry $entry -PayloadPath $payload -ExtractCache (Join-Path $runRoot 'inputs')
    foreach ($target in $targets) {
        $laneRoot = Join-Path $runRoot ("p/{0}/{1}" -f $entry.id, $target)
        New-Item -ItemType Directory -Path $laneRoot | Out-Null
        Write-Information "[$($entry.id) $target] compare source, build and installation" -InformationAction Continue
        $lane = Invoke-WorkflowBaselineLane $entry $target $sourceRoot $laneRoot $hostPaths[$target]
        $report.lanes.Add($lane)
        Write-Utf8Json $evidencePath $report
        Write-Information "[$($entry.id) $target] $($lane.status)" -InformationAction Continue
    }
}
foreach ($name in $fixtureIdentities.Keys) {
    $path = Join-Path $PSScriptRoot "Workflows/Unfamiliar/$name"
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fixtureIdentities[$name]) {
        throw "Workload fixture '$name' changed while the baseline was running."
    }
}
if ((Read-AssessmentPacket $PacketPath).Sha256 -ne $packetState.Sha256) {
    throw 'The workload packet changed while the baseline was running.'
}
$report.lanes | Group-Object { $_.status } | Select-Object Name,Count | Format-Table -AutoSize
