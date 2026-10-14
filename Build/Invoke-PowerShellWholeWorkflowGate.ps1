#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WorkspacePath,
    [Parameter(Mandatory)] [string] $EvidencePath,
    [string] $CliAssemblyPath,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows -or $PSVersionTable.PSVersion.Major -ne 7 -or $PSVersionTable.PSVersion.Minor -ne 6 -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'Whole-workflow qualification requires Windows x64 with PowerShell 7.6 and Windows PowerShell 5.1.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$powerShellHost = [Environment]::ProcessPath
$corpusRoot = Join-Path $repositoryRoot 'Benchmarks/PowerShellCompilation/Corpus'
. (Join-Path $corpusRoot 'Corpus.Runner.Common.ps1')
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if ((Test-Path -LiteralPath $EvidencePath) -and @(Get-ChildItem -LiteralPath $EvidencePath -Force).Count) { throw 'Choose an empty evidence directory; prior whole-workflow receipts must be preserved.' }
# This is a conservative fixture layout budget, not a product filesystem limit.
if ($WorkspacePath.Length -gt 25) { throw 'Choose a task-owned workspace root of at most 25 characters to leave room for installed net472 module payload paths.' }
if ($WorkspacePath.TrimEnd('\', '/') -eq [IO.Path]::GetPathRoot($WorkspacePath).TrimEnd('\', '/')) { throw 'Choose a task-owned workspace directory below the volume root.' }
if (-not (Test-Path -LiteralPath ([IO.Path]::GetPathRoot($WorkspacePath)) -PathType Container)) { throw 'The selected workspace volume is not mounted.' }
if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME) -or [string]::IsNullOrWhiteSpace($env:NUGET_SCRATCH) -or
    [string]::IsNullOrWhiteSpace($env:TEMP) -or
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE -ne 'false' -or $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH -ne 'false') {
    throw 'Use a guarded child environment with private DOTNET_CLI_HOME/TEMP, shared NUGET_SCRATCH, and certificate/global-tool generation disabled.'
}
if (-not $CliAssemblyPath) { $CliAssemblyPath = Join-Path $repositoryRoot 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
$CliAssemblyPath = [IO.Path]::GetFullPath($CliAssemblyPath)
if (-not (Test-Path -LiteralPath $CliAssemblyPath -PathType Leaf)) { throw "Build the source CLI first: $CliAssemblyPath" }
New-Item -ItemType Directory -Path $WorkspacePath, $EvidencePath -Force | Out-Null
$closureRoot = Split-Path $CliAssemblyPath
$identity = @(Get-ChildItem -LiteralPath $closureRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = [IO.Path]::GetRelativePath($closureRoot, $_.FullName).Replace('\', '/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant() }
})
Write-Utf8Json -Path (Join-Path $EvidencePath 'cli-identity.json') -Value ([ordered]@{ schemaVersion = 1; files = $identity })
$modules = @(
    @{ name = 'Yaml'; prefix = 'yaml'; publicName = 'powershell-yaml'; version = '0.4.12' },
    @{ name = 'PlatyPS'; prefix = 'platyps'; publicName = 'platyPS'; version = '0.14.2' }
)
$lanes = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$plannedLaneCount = $modules.Count * 2 + 1
$activeLaneId = $null
$failure = $null
# A lane contains eight CLI stages and three observers, each independently bounded
# at 900 seconds. Allow both 120-second fixture operations and 300 seconds for
# staging and receipt work. The outer watchdog covers the composed finite workflow.
$qualificationTimeoutSeconds = (11 * 900) + (2 * 120) + 300
try {
foreach ($module in $modules) {
    foreach ($framework in @('net10.0', 'net472')) {
        $packetPath = Join-Path $corpusRoot "Workflows/$($module.name)/$($module.prefix)-whole-module.$framework.json"
        $packet = Get-Content -LiteralPath $packetPath -Raw | ConvertFrom-Json
        if ($packet.schemaVersion -ne 1 -or @($packet.workloads).Count -ne 1) { throw 'Module qualification requires one reviewed workflow per packet.' }
        $entry = $packet.workloads[0]
        $pin = if ($module.name -eq 'Yaml') { $entry.origin } else { $entry.sourceProvenance }
        $uri = if ($module.name -eq 'Yaml') { $pin.packageUri } else { $pin.packageUrl }
        $allowlist = '^' + [regex]::Escape("https://www.powershellgallery.com/api/v2/package/$($module.publicName)/$($module.version)") + '$'
        $payload = Get-VerifiedCorpusPayload -Uri $uri -Sha256 $pin.packageSha256 -CacheRoot (Join-Path $WorkspacePath 'payloads') `
            -CacheExtension '.nupkg' -Label $module.publicName -AllowedUrlPattern $allowlist -OfflineMode:$Offline
        $sourceRoot = Join-Path $WorkspacePath $module.name
        if ($framework -eq 'net10.0') {
            Expand-VerifiedCorpusArchive -PayloadPath $payload -Target $sourceRoot -ContainmentRoot $WorkspacePath -Label $module.publicName
        }
        if (@(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File).Count -ne @($entry.sourceInputs).Count) { throw 'The public package inventory differs from the whole-module packet.' }
        $lanes.Add(@{ id = "$($module.prefix)-$framework"; packet = $packetPath; source = $sourceRoot; packageSha256 = $pin.packageSha256 })
    }
}
$lanes.Add(@{ id = 'sitemap-net10.0'; packet = (Join-Path $corpusRoot 'Workflows/CompareWebSitemaps/workflow.json'); source = (Join-Path $repositoryRoot 'Build'); packageSha256 = $null })
    foreach ($lane in $lanes) {
        $activeLaneId = $lane.id
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $laneEvidence = Join-Path $EvidencePath $lane.id
        $resultPath = Join-Path $laneEvidence 'qualification.json'
        New-Item -ItemType Directory -Path $laneEvidence -Force | Out-Null
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', (Join-Path $corpusRoot 'Invoke-ExternalQualification.ps1'),
            '-PacketPath', $lane.packet, '-SourceRoot', $lane.source, '-CliAssemblyPath', $CliAssemblyPath,
            '-WorkspacePath', $WorkspacePath, '-EvidencePath', $resultPath, '-DurableEvidencePath', $laneEvidence, '-AllowExternalExecution')
        if ($Offline) { $arguments += '-Offline' }
        $qualification = Invoke-OwnedProcess -FileName $powerShellHost -Arguments $arguments -WorkingDirectory $repositoryRoot `
            -TimeoutSeconds $qualificationTimeoutSeconds
        Write-Utf8Json -Path (Join-Path $laneEvidence 'qualifier.process.json') -Value $qualification
        if ($qualification.ExitCode -ne 0) { throw "Whole-workflow lane failed: $($lane.id); exit $($qualification.ExitCode)." }
        $receipt = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        if ($receipt.summary.total -ne 1 -or $receipt.summary.wholeArtifactWorkflowsQualified -ne 1 -or $receipt.summary.passed -ne 1) {
            throw "The lane did not qualify its complete declared workflow: $($lane.id)"
        }
        $clock.Stop()
        $results.Add([ordered]@{ id = $lane.id; packetSha256 = (Get-FileHash -LiteralPath $lane.packet).Hash.ToLowerInvariant();
            packageSha256 = $lane.packageSha256; declaredScenarios = $receipt.summary.declaredWorkflowScenarios; durationMilliseconds = $clock.ElapsedMilliseconds; passed = $true })
    }
    $activeLaneId = $null
} catch {
    $failure = [ordered]@{
        laneId = $activeLaneId
        type = $_.Exception.GetType().FullName
        message = $_.Exception.Message
        ownedProcess = $_.Exception.Data['PowerForgeOwnedProcess']
    }
    throw
} finally {
    $cliStable = @(Get-ChildItem -LiteralPath $closureRoot -Recurse -File).Count -eq $identity.Count
    foreach ($file in $identity) {
        $path = Join-Path $closureRoot $file.path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant() -ne $file.sha256) { $cliStable = $false }
    }
    Write-Utf8Json -Path (Join-Path $EvidencePath 'whole-workflow-gate.json') -Value ([ordered]@{
        schemaVersion = 1; passed = $results.Count -eq $plannedLaneCount -and $cliStable; compilerClosureUnchanged = $cliStable; plannedLanes = $plannedLaneCount; results = @($results)
        failure = $failure; qualificationTimeoutSeconds = $qualificationTimeoutSeconds
        interpretation = 'Pinned comparison inputs; canonical original/built/installed scenario parity and required owned CLR bodies. Hybrid retains the PowerShell SDK and supported hosted command regions.'
    })
    if (-not $cliStable) { throw 'The compiler CLI closure changed during whole-workflow qualification.' }
}
