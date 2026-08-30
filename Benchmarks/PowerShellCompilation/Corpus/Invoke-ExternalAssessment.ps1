[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'external-assessment.net10.json'),
    [string] $BaselinePath = (Join-Path $PSScriptRoot 'external-assessment-baseline.net10.json'),
    [string] $CliAssemblyPath,
    [string] $WorkspacePath,
    [string] $EvidencePath,
    [string[]] $WorkloadId,
    [int] $MaxArchiveEntries = 20000,
    [long] $MaxArchiveEntryBytes = 268435456,
    [long] $MaxArchiveTotalBytes = 1073741824,
    [double] $MaxArchiveCompressionRatio = 200.0,
    [switch] $Offline,
    [switch] $RefreshBaseline,
    [switch] $KeepRunArtifacts
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')

function Get-VerifiedPayload {
    param([object] $Entry, [string] $PayloadCache, [switch] $OfflineMode)

    if ($Entry.acquisition.kind -notin @('File', 'ZipArchive')) { throw "Unsupported acquisition kind for $($Entry.id): $($Entry.acquisition.kind)" }
    return Get-VerifiedCorpusPayload `
        -Uri ([Uri] $Entry.acquisition.url) `
        -Sha256 $Entry.acquisition.sha256 `
        -CacheRoot $PayloadCache `
        -CacheExtension '.payload' `
        -Label "assessment payload $($Entry.id)" `
        -OfflineMode:$OfflineMode
}

function Get-AssessmentSourceRoot {
    param(
        [object] $Entry,
        [string] $PayloadPath,
        [string] $ExtractCache,
        [int] $ArchiveEntryLimit,
        [long] $ArchiveEntryByteLimit,
        [long] $ArchiveTotalByteLimit,
        [double] $ArchiveCompressionRatioLimit
    )

    $target = Join-Path $ExtractCache $Entry.acquisition.sha256
    if ($Entry.acquisition.kind -eq 'ZipArchive') {
        Expand-VerifiedCorpusArchive -PayloadPath $PayloadPath -Target $target -ContainmentRoot $ExtractCache -Label "assessment payload $($Entry.id)" -EntryLimit $ArchiveEntryLimit -EntryByteLimit $ArchiveEntryByteLimit -TotalByteLimit $ArchiveTotalByteLimit -CompressionRatioLimit $ArchiveCompressionRatioLimit
    } else {
        Assert-ContainedPath -Root $ExtractCache -Path $target -Label 'Assessment extraction' | Out-Null
        if (Test-Path -LiteralPath $target) {
            $targetItem = Get-Item -LiteralPath $target -Force
            if (($targetItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Assessment extraction is a symbolic link or junction: $target" }
            Remove-Item -LiteralPath $target -Recurse -Force
        }
        New-Item -ItemType Directory -Path $target | Out-Null
        $destination = Assert-ContainedPath -Root $target -Path (Join-Path $target $Entry.entryPoint) -Label 'Assessment file entry point'
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $PayloadPath -Destination $destination
    }
    return $target
}

function Invoke-Census {
    param([string] $EntryPoint, [string] $TargetFramework, [string] $WorkingDirectory)

    $process = Invoke-OwnedProcess -FileName 'dotnet' -Arguments @($CliAssemblyPath, 'powershell', 'census', $EntryPoint, '--framework', $TargetFramework, '--output', 'json') -WorkingDirectory $WorkingDirectory
    try { $json = $process.StandardOutput | ConvertFrom-Json }
    catch { throw "PowerForge census did not return JSON. Exit=$($process.ExitCode); stderr=$($process.StandardError)" }
    if ($process.ExitCode -ne 0 -or -not $json.success) {
        $message = if ($json.error) { [string] $json.error } else { [string] $process.StandardError }
        throw "PowerForge census failed with exit $($process.ExitCode): $message"
    }
    return [pscustomobject]@{ Product = $json.result.products[0]; DurationMilliseconds = $process.DurationMilliseconds }
}

function ConvertTo-Baseline {
    param([object] $Packet, [string] $PacketSha256, [object[]] $Results)

    return [ordered]@{
        schemaVersion = 1
        packetId = $Packet.packetId
        packetSha256 = $PacketSha256
        recordedOn = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-dd')
        semanticProfile = $Packet.semanticProfile
        targetFramework = $Packet.targetFramework
        workloads = @($Results | Where-Object succeeded | ForEach-Object {
            [ordered]@{
                id = $_.id
                workloadKind = $_.workloadKind
                scenarioFamily = $_.scenarioFamily
                acquisitionSha256 = $_.acquisitionSha256
                sourceFingerprint = $_.sourceFingerprint
                sourceFiles = $_.sourceFiles
                totalUnits = $_.totalUnits
                emittedUnits = $_.emittedUnits
                runtimeFallbackUnits = $_.runtimeFallbackUnits
                parseErrorFiles = $_.parseErrorFiles
                postEmissionEvaluated = $_.postEmissionEvaluated
                totalFunctions = $_.totalFunctions
                analyzerEligibleFunctions = $_.analyzerEligibleFunctions
                emittedFunctions = $_.emittedFunctions
                droppedEligibleFunctions = $_.droppedEligibleFunctions
                fallbackFunctions = $_.fallbackFunctions
            }
        })
        interpretation = [ordered]@{
            powerShellLanguageCoveragePercentage = $null
            note = 'This is a pinned workload regression baseline. Emitted-unit and emitted-function ratios are not estimates of PowerShell-language coverage or proof of complete workload execution.'
        }
    }
}

function Compare-Baseline {
    param([object] $Baseline, [object] $Packet, [string] $PacketSha256, [object[]] $Results, [string[]] $SelectedIds)

    $regressions = [Collections.Generic.List[object]]::new()
    foreach ($identity in @(
        @{ Metric = 'schemaVersion'; Expected = 1; Actual = $Baseline.schemaVersion },
        @{ Metric = 'packetId'; Expected = $Packet.packetId; Actual = $Baseline.packetId },
        @{ Metric = 'semanticProfile'; Expected = $Packet.semanticProfile; Actual = $Baseline.semanticProfile },
        @{ Metric = 'targetFramework'; Expected = $Packet.targetFramework; Actual = $Baseline.targetFramework }
    )) {
        if ($identity.Actual -ne $identity.Expected) {
            $regressions.Add([ordered]@{ id = '<baseline>'; metric = $identity.Metric; expected = $identity.Expected; actual = $identity.Actual })
        }
    }
    if ($Baseline.packetSha256 -ne $PacketSha256) {
        $regressions.Add([ordered]@{ id = '<packet>'; metric = 'packetSha256'; expected = $Baseline.packetSha256; actual = $PacketSha256 })
    }
    $packetIds = @($Packet.workloads.id | Sort-Object -Unique)
    $baselineIds = @($Baseline.workloads.id)
    foreach ($id in $packetIds) {
        $count = @($baselineIds | Where-Object { $_ -eq $id }).Count
        if ($count -ne 1) { $regressions.Add([ordered]@{ id = $id; metric = 'baselineRowCount'; expected = 1; actual = $count }) }
    }
    foreach ($id in @($baselineIds | Where-Object { $_ -notin $packetIds } | Sort-Object -Unique)) {
        $regressions.Add([ordered]@{ id = $id; metric = 'declaredPacketWorkload'; expected = $true; actual = $false })
    }
    if ($regressions.Count -gt 0) { return @($regressions) }

    $expectedWorkloads = @($Baseline.workloads | Where-Object { -not $SelectedIds -or $_.id -in $SelectedIds })
    foreach ($expected in $expectedWorkloads) {
        $actual = @($Results | Where-Object id -EQ $expected.id)
        if ($actual.Count -ne 1 -or -not $actual[0].succeeded) {
            $regressions.Add([ordered]@{ id = $expected.id; metric = 'successfulAssessment'; expected = $true; actual = $false })
            continue
        }
        $current = $actual[0]
        foreach ($metric in @('acquisitionSha256', 'sourceFingerprint', 'sourceFiles', 'totalUnits', 'totalFunctions')) {
            if ($current.$metric -ne $expected.$metric) {
                $regressions.Add([ordered]@{ id = $expected.id; metric = $metric; expected = $expected.$metric; actual = $current.$metric })
            }
        }
        foreach ($metric in @('emittedUnits', 'emittedFunctions')) {
            if ([int] $current.$metric -lt [int] $expected.$metric) {
                $regressions.Add([ordered]@{ id = $expected.id; metric = $metric; expectedMinimum = $expected.$metric; actual = $current.$metric })
            }
        }
        foreach ($metric in @('runtimeFallbackUnits', 'parseErrorFiles', 'droppedEligibleFunctions', 'fallbackFunctions')) {
            if ([int] $current.$metric -gt [int] $expected.$metric) {
                $regressions.Add([ordered]@{ id = $expected.id; metric = $metric; expectedMaximum = $expected.$metric; actual = $current.$metric })
            }
        }
        if ($expected.postEmissionEvaluated -ne $true -or $current.postEmissionEvaluated -ne $true) {
            $regressions.Add([ordered]@{ id = $expected.id; metric = 'postEmissionEvaluated'; expected = $true; actual = $current.postEmissionEvaluated })
        }
    }
    return @($regressions)
}

$PacketPath = [IO.Path]::GetFullPath($PacketPath)
$BaselinePath = [IO.Path]::GetFullPath($BaselinePath)
$packet = Get-Content -LiteralPath $PacketPath -Raw | ConvertFrom-Json
if ($packet.schemaVersion -ne 1) { throw "Unsupported external-assessment schema $($packet.schemaVersion)." }
if (@($packet.workloads).Count -eq 0) { throw 'The assessment packet contains no workloads.' }
if (@($packet.workloads.id | Sort-Object -Unique).Count -ne @($packet.workloads).Count) { throw 'Assessment workload ids must be unique.' }
if ($MaxArchiveEntries -le 0 -or $MaxArchiveEntryBytes -le 0 -or $MaxArchiveTotalBytes -le 0 -or $MaxArchiveCompressionRatio -le 0) {
    throw 'Archive expansion limits must be positive.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $CliAssemblyPath) { $CliAssemblyPath = Join-Path $repositoryRoot 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
$CliAssemblyPath = [IO.Path]::GetFullPath($CliAssemblyPath)
if (-not (Test-Path -LiteralPath $CliAssemblyPath -PathType Leaf)) { throw "Build the PowerForge CLI first: $CliAssemblyPath" }
if (-not $WorkspacePath) { $WorkspacePath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/ExternalAssessment' }
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
New-Item -ItemType Directory -Path $WorkspacePath -Force | Out-Null
$payloadCache = Join-Path $WorkspacePath 'payloads'
$extractCache = Join-Path $WorkspacePath 'extract'
New-Item -ItemType Directory -Path $payloadCache, $extractCache -Force | Out-Null
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $WorkspacePath ('runs/' + $runId)
Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Assessment run' | Out-Null
New-Item -ItemType Directory -Path $runRoot | Out-Null
if (-not $EvidencePath) { $EvidencePath = Join-Path $WorkspacePath ('evidence/' + $runId + '.json') }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
$packetSha256 = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
$results = [Collections.Generic.List[object]]::new()
$frontierRows = [Collections.Generic.List[object]]::new()

try {
    $selected = @($packet.workloads)
    if ($WorkloadId) { $selected = @($selected | Where-Object { $_.id -in $WorkloadId }) }
    $unknownIds = @($WorkloadId | Where-Object { $_ -notin @($packet.workloads.id) } | Sort-Object -Unique)
    if ($unknownIds.Count -gt 0) { throw "Unknown assessment workload id(s): $($unknownIds -join ', ')" }
    foreach ($entry in $selected) {
        Write-Information "[$($entry.id)] acquire, verify, and census" -InformationAction Continue
        try {
            $payloadPath = Get-VerifiedPayload -Entry $entry -PayloadCache $payloadCache -OfflineMode:$Offline
            $sourceRoot = Get-AssessmentSourceRoot -Entry $entry -PayloadPath $payloadPath -ExtractCache $extractCache -ArchiveEntryLimit $MaxArchiveEntries -ArchiveEntryByteLimit $MaxArchiveEntryBytes -ArchiveTotalByteLimit $MaxArchiveTotalBytes -ArchiveCompressionRatioLimit $MaxArchiveCompressionRatio
            $entryPoint = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $entry.entryPoint) -Label 'Assessment entry point'
            if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) { throw "Assessment entry point does not exist: $entryPoint" }
            $census = Invoke-Census -EntryPoint $entryPoint -TargetFramework $packet.targetFramework -WorkingDirectory $runRoot
            $product = $census.Product
            $functionPercentage = if ($product.coverage.totalFunctions) { [Math]::Round(100 * $product.coverage.emittedFunctions / $product.coverage.totalFunctions, 2) } else { $null }
            $unitPercentage = if ($product.totalUnits) { [Math]::Round(100 * $product.compilableUnits / $product.totalUnits, 2) } else { 0.0 }
            foreach ($impact in @($product.functionImpacts)) {
                $frontierRows.Add([pscustomobject]@{ WorkloadId = $entry.id; Impact = $impact })
            }
            $results.Add([ordered]@{
                id = $entry.id
                workloadKind = $entry.workloadKind
                scenarioFamily = $entry.scenarioFamily
                revisionKind = $entry.revisionKind
                revision = $entry.revision
                acquisitionSha256 = $entry.acquisition.sha256
                sourceFingerprint = $product.sourceFingerprint
                sourceFiles = $product.sourceFiles
                totalUnits = $product.totalUnits
                emittedUnits = $product.compilableUnits
                runtimeFallbackUnits = $product.runtimeFallbackUnits
                parseErrorFiles = $product.parseErrorFiles
                postEmissionEvaluated = $product.coverage.postEmissionEvaluated
                totalFunctions = $product.coverage.totalFunctions
                analyzerEligibleFunctions = $product.coverage.analyzerEligibleFunctions
                emittedFunctions = $product.coverage.emittedFunctions
                droppedEligibleFunctions = $product.coverage.droppedEligibleFunctions
                fallbackFunctions = $product.coverage.fallbackFunctions
                emittedUnitPercentage = $unitPercentage
                emittedFunctionPercentage = $functionPercentage
                leadingBlockers = @($product.blockers | Select-Object -First 12)
                functionFrontier = @($product.functionImpacts | Select-Object -First 12)
                analysisMilliseconds = $census.DurationMilliseconds
                assessmentOnly = $true
                completeWorkloadExecution = $false
                succeeded = $true
            })
        } catch {
            $results.Add([ordered]@{ id = $entry.id; workloadKind = $entry.workloadKind; scenarioFamily = $entry.scenarioFamily; succeeded = $false; error = $_.Exception.Message })
        }
    }

    if ($RefreshBaseline) {
        if ($results.Count -ne @($packet.workloads).Count -or @($results | Where-Object { -not $_.succeeded }).Count -gt 0) {
            throw 'A baseline refresh requires every declared workload to complete successfully.'
        }
        Write-Utf8Json -Path $BaselinePath -Value (ConvertTo-Baseline -Packet $packet -PacketSha256 $packetSha256 -Results @($results))
    }
    if (-not (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) { throw "Assessment baseline does not exist: $BaselinePath" }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $regressions = Compare-Baseline -Baseline $baseline -Packet $packet -PacketSha256 $packetSha256 -Results @($results) -SelectedIds $WorkloadId
    $successful = @($results | Where-Object succeeded)
    $sourceFiles = [int] (($successful | ForEach-Object { [int] $_.sourceFiles } | Measure-Object -Sum).Sum)
    $totalUnits = [int] (($successful | ForEach-Object { [int] $_.totalUnits } | Measure-Object -Sum).Sum)
    $emittedUnits = [int] (($successful | ForEach-Object { [int] $_.emittedUnits } | Measure-Object -Sum).Sum)
    $totalFunctions = [int] (($successful | ForEach-Object { [int] $_.totalFunctions } | Measure-Object -Sum).Sum)
    $emittedFunctions = [int] (($successful | ForEach-Object { [int] $_.emittedFunctions } | Measure-Object -Sum).Sum)
    $parseErrorFiles = [int] (($successful | ForEach-Object { [int] $_.parseErrorFiles } | Measure-Object -Sum).Sum)
    $crossWorkloadFrontier = @($frontierRows |
        Group-Object { $_.Impact.featureId } |
        ForEach-Object {
            $group = @($_.Group)
            [pscustomobject][ordered]@{
                featureId = $_.Name
                title = $group[0].Impact.title
                affectedWorkloads = @($group.WorkloadId | Sort-Object -Unique).Count
                occurrences = [int] (($group | ForEach-Object { [int] $_.Impact.occurrences } | Measure-Object -Sum).Sum)
                affectedUnits = [int] (($group | ForEach-Object { [int] $_.Impact.affectedUnits } | Measure-Object -Sum).Sum)
                visibleSoleBlockerUnits = [int] (($group | ForEach-Object { [int] $_.Impact.visibleSoleBlockerUnits } | Measure-Object -Sum).Sum)
                recommendation = $group[0].Impact.recommendation
            }
        } |
        Sort-Object -Property @{ Expression = 'affectedWorkloads'; Descending = $true }, @{ Expression = 'visibleSoleBlockerUnits'; Descending = $true }, @{ Expression = 'affectedUnits'; Descending = $true }, featureId |
        Select-Object -First 20)
    $evidence = [ordered]@{
        schemaVersion = 1
        packetId = $packet.packetId
        packetSha256 = $packetSha256
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        semanticProfile = $packet.semanticProfile
        targetFramework = $packet.targetFramework
        host = [ordered]@{
            operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            powerShell = $PSVersionTable.PSVersion.ToString()
            dotnet = (& dotnet --version)
        }
        workloads = @($results)
        crossWorkloadFunctionFrontier = $crossWorkloadFrontier
        regressions = @($regressions)
        summary = [ordered]@{
            workloadsAssessed = $successful.Count
            workloadsTotal = $results.Count
            sourceFiles = $sourceFiles
            totalUnits = $totalUnits
            emittedUnits = $emittedUnits
            emittedUnitPercentage = if ($totalUnits) { [Math]::Round(100 * $emittedUnits / $totalUnits, 2) } else { 0.0 }
            totalFunctions = $totalFunctions
            emittedFunctions = $emittedFunctions
            emittedFunctionPercentage = if ($totalFunctions) { [Math]::Round(100 * $emittedFunctions / $totalFunctions, 2) } else { $null }
            parseErrorFiles = $parseErrorFiles
            acquisitionOrAnalysisFailures = @($results | Where-Object { -not $_.succeeded }).Count
            regressions = $regressions.Count
            completeWorkloadExecutions = 0
            powerShellLanguageCoveragePercentage = $null
        }
        interpretation = 'Assessment success means pinned acquisition and post-emission census completed without regression. It does not mean the workload compiled completely or executed without PowerShell.'
    }
    Write-Utf8Json -Path $EvidencePath -Value $evidence
    Write-Information "Evidence: $EvidencePath" -InformationAction Continue
    Write-Information "Assessed: $($successful.Count)/$($results.Count); emitted functions: $emittedFunctions/$totalFunctions; emitted units: $emittedUnits/$totalUnits; regressions: $($regressions.Count)" -InformationAction Continue
    if ($evidence.summary.acquisitionOrAnalysisFailures -gt 0 -or $regressions.Count -gt 0) { exit 1 }
} finally {
    if (-not $KeepRunArtifacts -and (Test-Path -LiteralPath $runRoot)) {
        Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Assessment run cleanup' | Out-Null
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
