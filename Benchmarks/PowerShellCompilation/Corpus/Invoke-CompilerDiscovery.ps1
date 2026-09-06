#requires -Version 7.4
[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'compiler-discovery.net10.json'),
    [string] $CliAssemblyPath = (Join-Path $PSScriptRoot '../../../PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll'),
    [string] $WorkspacePath = (Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/CompilerDiscovery'),
    [string[]] $WorkloadId,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
$packet = Get-Content -LiteralPath $PacketPath -Raw | ConvertFrom-Json
if ($packet.schemaVersion -ne 1) { throw 'Unsupported compiler discovery packet schema.' }
$CliAssemblyPath = [IO.Path]::GetFullPath($CliAssemblyPath)
if (-not (Test-Path -LiteralPath $CliAssemblyPath -PathType Leaf)) { throw "Build the CLI first: $CliAssemblyPath" }
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
$payloadCache = Join-Path $WorkspacePath 'payloads'
$extractCache = Join-Path $WorkspacePath 'extract'
$evidenceRoot = Join-Path $WorkspacePath ('evidence/' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
New-Item -ItemType Directory -Path $payloadCache, $extractCache, $evidenceRoot -Force | Out-Null
$selected = @($packet.workloads | Where-Object { -not $WorkloadId -or $_.id -in $WorkloadId })
if ($WorkloadId -and @($WorkloadId | Where-Object { $_ -notin $packet.workloads.id }).Count) { throw 'Unknown discovery workload ID.' }
$results = [Collections.Generic.List[object]]::new()
foreach ($entry in $selected) {
    Write-Information "[$($entry.id)] verify pinned source and discover inputs" -InformationAction Continue
    $payload = Get-VerifiedCorpusPayload -Uri $entry.archiveUrl -Sha256 $entry.sha256 -CacheRoot $payloadCache -CacheExtension '.zip' -Label $entry.id -OfflineMode:$Offline
    $sourceRoot = Join-Path $extractCache $entry.sha256
    Expand-VerifiedCorpusArchive -PayloadPath $payload -Target $sourceRoot -ContainmentRoot $extractCache -Label $entry.id
    $entryPoint = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $entry.entryPoint) -Label 'Discovery entry point'
    if ($entry.selection -eq 'Scripts') {
        $inputs = @(Get-ChildItem -LiteralPath $entryPoint -Recurse -File -Filter '*.ps1' | Where-Object {
            $relative = [IO.Path]::GetRelativePath($entryPoint, $_.FullName).Replace('\', '/')
            (-not $packet.excludeTestScripts -or $_.Name -notlike '*.tests.ps1') -and
            (-not $packet.excludeHiddenPaths -or $relative -notmatch '(^|/)\.')
        } | Select-Object -ExpandProperty FullName | Sort-Object)
    } elseif ($entry.selection -eq 'Module') { $inputs = @($entryPoint) }
    else { throw "Unknown source selection: $($entry.selection)" }
    if (-not $inputs.Count) { throw "No source inputs for $($entry.id)." }
    foreach ($mode in $entry.modes) {
        $products = [Collections.Generic.List[object]]::new()
        $failures = [Collections.Generic.List[object]]::new()
        # Bound command-line length and process startup overhead without merging
        # unrelated scripts into a synthetic compilation unit.
        for ($offset = 0; $offset -lt $inputs.Count; $offset += 16) {
            $arguments = @($CliAssemblyPath, 'powershell', 'census', '--framework', $packet.targetFramework,
                '--kind', $entry.artifactKind, '--mode', $mode, '--semantic-profile', $packet.semanticProfile, '--output', 'json')
            foreach ($inputPath in @($inputs | Select-Object -Skip $offset -First 16)) { $arguments += @('--path', $inputPath) }
            $process = Invoke-OwnedProcess -FileName 'dotnet' -Arguments $arguments -WorkingDirectory $WorkspacePath
            $json = $process.StandardOutput | ConvertFrom-Json
            if ($null -eq $json.result.products -or $null -eq $json.result.inputFailures) {
                throw "Census failed before producing per-input evidence: $($json.error) $($process.StandardError)"
            }
            Write-Utf8Json -Path (Join-Path $evidenceRoot "$($entry.id)-$mode-$offset.json") -Value $json.result
            foreach ($product in $json.result.products) { $products.Add($product) }
            foreach ($failure in $json.result.inputFailures) {
                $failures.Add([ordered]@{ path = [IO.Path]::GetRelativePath($sourceRoot, $failure.path).Replace('\', '/'); errorType = $failure.errorType; message = $failure.message.Replace($sourceRoot, '<source>') })
            }
        }
        $summary = [ordered]@{
            id = $entry.id; revision = $entry.revision; archiveSha256 = $entry.sha256
            artifactKind = $entry.artifactKind; mode = $mode; semanticProfile = $packet.semanticProfile
            submittedInputs = $inputs.Count; assessedInputs = $products.Count; failedInputs = $failures.Count
            sourceFiles = [int](($products.sourceFiles | Measure-Object -Sum).Sum)
            totalUnits = [int](($products.totalUnits | Measure-Object -Sum).Sum)
            emittedUnits = [int](($products.compilableUnits | Measure-Object -Sum).Sum)
            totalFunctions = [int](($products.coverage.totalFunctions | Measure-Object -Sum).Sum)
            emittedFunctions = [int](($products.coverage.emittedFunctions | Measure-Object -Sum).Sum)
            parseErrorFiles = [int](($products.parseErrorFiles | Measure-Object -Sum).Sum)
            failures = @($failures); completeWorkloadExecutions = 0
        }
        $results.Add($summary)
        Write-Information "[$($entry.id)/$mode] $($products.Count)/$($inputs.Count) inputs assessed, $($failures.Count) failures, $($summary.emittedUnits)/$($summary.totalUnits) units emitted" -InformationAction Continue
    }
}
$report = [ordered]@{
    schemaVersion = 1; packetId = $packet.packetId
    packetSha256 = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
    targetFramework = $packet.targetFramework; results = @($results)
    interpretation = 'Discovery only. Per-input failures remain in submitted counts. Source totals sum independent closures and can repeat shared files. No source was imported or executed.'
}
$reportPath = Join-Path $evidenceRoot 'summary.json'
Write-Utf8Json -Path $reportPath -Value $report
Write-Output $reportPath
