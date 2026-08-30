[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'public-corpus.net8.json'),
    [string] $BaselinePath = (Join-Path $PSScriptRoot 'public-corpus-baseline.net8.json'),
    [string] $CliAssemblyPath,
    [string] $WorkspacePath,
    [string] $EvidencePath,
    [string[]] $ModuleId,
    [string] $RuntimeIdentifier,
    [switch] $Offline,
    [switch] $SkipModules,
    [switch] $SkipStrictPrograms,
    [switch] $KeepRunArtifacts
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')

function Invoke-PowerForgeJson {
    param([string[]] $Arguments, [string] $WorkingDirectory, [int] $TimeoutSeconds = 900)

    $result = Invoke-OwnedProcess -FileName 'dotnet' -Arguments (@($CliAssemblyPath) + $Arguments + @('--output', 'json')) -WorkingDirectory $WorkingDirectory -Environment @{} -TimeoutSeconds $TimeoutSeconds
    try {
        $json = $result.StandardOutput | ConvertFrom-Json
    } catch {
        $tail = if ($result.StandardOutput.Length -gt 4000) { $result.StandardOutput.Substring($result.StandardOutput.Length - 4000) } else { $result.StandardOutput }
        throw "PowerForge did not return JSON. Exit=$($result.ExitCode). stdout tail: $tail stderr: $($result.StandardError)"
    }
    if ($result.ExitCode -ne 0 -or -not $json.success) {
        $message = if ($json.error) { [string] $json.error } else { [string] $result.StandardError }
        throw "PowerForge command failed with exit $($result.ExitCode): $message"
    }
    return [pscustomobject]@{ Json = $json; DurationMilliseconds = $result.DurationMilliseconds }
}

function Get-VerifiedPackage {
    param([object] $Entry, [string] $PackageCache)

    return Get-VerifiedCorpusPayload `
        -Uri ([Uri] $Entry.packageUrl) `
        -Sha256 $Entry.sha256 `
        -CacheRoot $PackageCache `
        -CacheExtension '.nupkg' `
        -Label "package $($Entry.id) $($Entry.version)" `
        -OfflineMode:$Offline `
        -AllowedUrlPattern '^https://www\.powershellgallery\.com/api/v2/package/'
}

function Expand-VerifiedPackage {
    param([object] $Entry, [string] $PackagePath, [string] $ExtractCache)

    $target = Join-Path $ExtractCache $Entry.sha256
    Expand-VerifiedCorpusArchive -PayloadPath $PackagePath -Target $target -ContainmentRoot $ExtractCache -Label "package $($Entry.id)"
    Write-Utf8Json -Path (Join-Path $target '.powerforge-corpus-package.json') -Value ([ordered]@{
        schemaVersion = 1
        id = $Entry.id
        version = $Entry.version
        sha256 = $Entry.sha256
    })
    return $target
}

function Invoke-CleanModuleProbe {
    param([string] $ManifestPath, [string] $ProbeScript, [string] $WorkingDirectory)

    $childScript = @'
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$manifestPath = $env:POWERFORGE_CORPUS_MANIFEST
$probeText = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($env:POWERFORGE_CORPUS_PROBE))
$module = Import-Module -Name $manifestPath -Force -PassThru -ErrorAction Stop
$probeOutput = @(& ([scriptblock]::Create($probeText)))
if ('ok' -notin $probeOutput) { throw 'The declared corpus probe did not return its success marker.' }
[pscustomobject]@{
    moduleName = $module.Name
    moduleVersion = $module.Version.ToString()
    exportedCommands = $module.ExportedCommands.Count
    probeSucceeded = $true
} | ConvertTo-Json -Compress
'@
    $encodedChild = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))
    $environment = @{
        PSModulePath = (Join-Path $PSHOME 'Modules')
        POWERSHELL_TELEMETRY_OPTOUT = '1'
        POWERFORGE_CORPUS_MANIFEST = $ManifestPath
        POWERFORGE_CORPUS_PROBE = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($ProbeScript))
    }
    $process = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedChild) -WorkingDirectory $WorkingDirectory -Environment $environment -TimeoutSeconds 180
    if ($process.ExitCode -ne 0) {
        throw "Clean module probe failed with exit $($process.ExitCode): $($process.StandardError) $($process.StandardOutput)"
    }
    $line = $process.StandardOutput -split "`r?`n" | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1
    if (-not $line) { throw "Clean module probe returned no JSON evidence: $($process.StandardOutput)" }
    return [pscustomobject]@{
        Json = ($line | ConvertFrom-Json)
        DurationMilliseconds = $process.DurationMilliseconds
        StandardError = $process.StandardError.Trim()
    }
}

function Get-LedgerSummary {
    param([object] $Ledger)

    $entries = @($Ledger.entries)
    $causes = $entries | ForEach-Object { @($_.diagnosticChain) } | Where-Object featureId |
        Group-Object featureId | Sort-Object -Property @{ Expression = 'Count'; Descending = $true }, Name | ForEach-Object {
            [ordered]@{ featureId = $_.Name; units = $_.Count }
        }
    return [ordered]@{
        analyzedUnits = $entries.Count
        boundUnits = @($entries | Where-Object semanticEligible).Count
        emittedClrUnits = @($entries | Where-Object emittedClrMethod).Count
        exportedCmdletUnits = @($entries | Where-Object emittedBinaryCmdlet).Count
        hostedRegions = ($entries | Measure-Object runtimeCommandRegions -Sum).Sum
        retainedSourceUnits = @($entries | Where-Object retainedHostedSource).Count
        semanticFallbackUnits = @($entries | Where-Object { -not $_.semanticEligible }).Count
        shapingFallbackUnits = @($entries | Where-Object shapingFallback).Count
        runtimeRoutedUnits = @($entries | Where-Object { $_.retainedHostedSource -or [int]$_.runtimeCommandRegions -gt 0 }).Count
        omittedUnits = @($entries | Where-Object omitted).Count
        rejectedUnits = @($entries | Where-Object rejected).Count
        leadingFallbackCauses = @($causes | Select-Object -First 12)
    }
}

function New-TargetContract {
    param([string] $Rid, [string] $TargetFramework, [string] $SemanticProfileId, [string] $Path)

    $parts = $Rid.Split('-')
    Write-Utf8Json -Path $Path -Value ([ordered]@{
        schemaVersion = 3
        artifactKind = 'Executable'
        mode = 'Strict'
        targetFramework = $TargetFramework
        runtimeIdentifier = $Rid
        operatingSystem = $parts[0]
        architecture = $parts[-1]
        runtimeRequirement = 'DotNet'
        deployment = 'FrameworkDependent'
        singleFile = $true
        allowsPowerShellRuntimeEvaluation = $false
        explicit = $true
        supportLevel = 'Supported'
        semanticProfileId = $SemanticProfileId
        contractSha256 = ''
    })
}

function Get-Sum {
    param([object[]] $Values)
    return [int] (($Values | Measure-Object -Sum).Sum)
}

function Compare-PublicBaseline {
    param(
        [object] $Baseline,
        [object] $Packet,
        [string] $PacketSha256,
        [object[]] $Modules,
        [object[]] $StrictPrograms,
        [string] $Rid,
        [bool] $CompareModules,
        [bool] $CompareStrict
    )

    $regressions = [Collections.Generic.List[object]]::new()
    foreach ($identity in @(
        @{ Metric = 'schemaVersion'; Expected = 1; Actual = $Baseline.schemaVersion },
        @{ Metric = 'packetId'; Expected = $Packet.packetId; Actual = $Baseline.packetId },
        @{ Metric = 'packetSha256'; Expected = $PacketSha256; Actual = $Baseline.packetSha256 },
        @{ Metric = 'semanticProfile'; Expected = $Packet.semanticProfile; Actual = $Baseline.semanticProfile }
    )) {
        if ($identity.Actual -ne $identity.Expected) {
            $regressions.Add([ordered]@{ scope = 'identity'; metric = $identity.Metric; expected = $identity.Expected; actual = $identity.Actual })
        }
    }
    if ($regressions.Count -gt 0) { return @($regressions) }

    if ($CompareModules) {
        if ($Rid -ne $Baseline.hybrid.runtimeIdentifier) {
            $regressions.Add([ordered]@{ scope = 'hybrid'; metric = 'runtimeIdentifier'; expected = $Baseline.hybrid.runtimeIdentifier; actual = $Rid })
        }
        $successful = @($Modules | Where-Object succeeded)
        $metrics = [ordered]@{
            modulesPassed = $successful.Count
            modulesTotal = $Modules.Count
            scenarioFamilies = @($successful.scenarioFamily | Sort-Object -Unique).Count
            cleanImportedCommands = Get-Sum @($successful.exportedCommands)
            analyzedUnits = Get-Sum @($successful.counts.analyzedUnits)
            boundUnits = Get-Sum @($successful.counts.boundUnits)
            emittedClrUnits = Get-Sum @($successful.counts.emittedClrUnits)
            exportedCmdletUnits = Get-Sum @($successful.counts.exportedCmdletUnits)
            hostedRegions = Get-Sum @($successful.counts.hostedRegions)
            retainedSourceUnits = Get-Sum @($successful.counts.retainedSourceUnits)
            semanticFallbackUnits = Get-Sum @($successful.counts.semanticFallbackUnits)
            shapingFallbackUnits = Get-Sum @($successful.counts.shapingFallbackUnits)
            runtimeRoutedUnits = Get-Sum @($successful.counts.runtimeRoutedUnits)
            omittedUnits = Get-Sum @($successful.counts.omittedUnits)
            rejectedUnits = Get-Sum @($successful.counts.rejectedUnits)
        }
        foreach ($metric in @('modulesPassed', 'modulesTotal', 'scenarioFamilies', 'cleanImportedCommands', 'analyzedUnits')) {
            if ([int] $metrics[$metric] -ne [int] $Baseline.hybrid.$metric) {
                $regressions.Add([ordered]@{ scope = 'hybrid'; metric = $metric; expected = $Baseline.hybrid.$metric; actual = $metrics[$metric] })
            }
        }
        foreach ($metric in @('boundUnits', 'emittedClrUnits', 'exportedCmdletUnits')) {
            if ([int] $metrics[$metric] -lt [int] $Baseline.hybrid.$metric) {
                $regressions.Add([ordered]@{ scope = 'hybrid'; metric = $metric; expectedMinimum = $Baseline.hybrid.$metric; actual = $metrics[$metric] })
            }
        }
        foreach ($metric in @('hostedRegions', 'retainedSourceUnits', 'semanticFallbackUnits', 'shapingFallbackUnits', 'runtimeRoutedUnits', 'omittedUnits', 'rejectedUnits')) {
            if ([int] $metrics[$metric] -gt [int] $Baseline.hybrid.$metric) {
                $regressions.Add([ordered]@{ scope = 'hybrid'; metric = $metric; expectedMaximum = $Baseline.hybrid.$metric; actual = $metrics[$metric] })
            }
        }
    }

    if ($CompareStrict) {
        $targetHost = @($Baseline.strict.targetHosts | Where-Object runtimeIdentifier -eq $Rid)
        if ($targetHost.Count -ne 1) {
            $regressions.Add([ordered]@{ scope = 'strict'; metric = 'targetHost'; expected = $Rid; actual = @($Baseline.strict.targetHosts.runtimeIdentifier) -join ',' })
        } else {
            $successful = @($StrictPrograms | Where-Object succeeded)
            $metrics = [ordered]@{
                programs = $StrictPrograms.Count
                programsPassed = $successful.Count
                analyzedUnits = Get-Sum @($successful.counts.analyzedUnits)
                boundUnits = Get-Sum @($successful.counts.boundUnits)
                emittedClrUnits = Get-Sum @($successful.counts.emittedClrUnits)
            }
            foreach ($metric in @('programs', 'analyzedUnits')) {
                if ([int] $metrics[$metric] -ne [int] $Baseline.strict.$metric) {
                    $regressions.Add([ordered]@{ scope = 'strict'; metric = $metric; expected = $Baseline.strict.$metric; actual = $metrics[$metric] })
                }
            }
            foreach ($metric in @('boundUnits', 'emittedClrUnits')) {
                if ([int] $metrics[$metric] -lt [int] $Baseline.strict.$metric) {
                    $regressions.Add([ordered]@{ scope = 'strict'; metric = $metric; expectedMinimum = $Baseline.strict.$metric; actual = $metrics[$metric] })
                }
            }
            if ($metrics.programsPassed -ne $targetHost[0].programsPassed -or $metrics.programs -ne $targetHost[0].programsTotal) {
                $regressions.Add([ordered]@{ scope = 'strict'; metric = 'targetHostPrograms'; expected = "$($targetHost[0].programsPassed)/$($targetHost[0].programsTotal)"; actual = "$($metrics.programsPassed)/$($metrics.programs)" })
            }
        }
    }
    return @($regressions)
}

function Get-CurrentRuntimeIdentifier {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { return "win-$architecture" }
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { return "osx-$architecture" }
    return "linux-$architecture"
}

$PacketPath = [IO.Path]::GetFullPath($PacketPath)
$BaselinePath = [IO.Path]::GetFullPath($BaselinePath)
$packet = Get-Content -LiteralPath $PacketPath -Raw | ConvertFrom-Json
if ($packet.schemaVersion -ne 1) { throw "Unsupported public-corpus schema $($packet.schemaVersion)." }
if (@($packet.modules).Count -lt 10) { throw 'The fixed public packet must contain at least ten modules.' }
if (@($packet.modules.scenarioFamily | Sort-Object -Unique).Count -lt 5) { throw 'The fixed public packet must span at least five scenario families.' }
if (@($packet.strictPrograms).Count -lt 3) { throw 'The fixed packet must contain at least three Strict programs.' }

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
if (-not $CliAssemblyPath) { $CliAssemblyPath = Join-Path $repositoryRoot 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
$CliAssemblyPath = [IO.Path]::GetFullPath($CliAssemblyPath)
if (-not (Test-Path -LiteralPath $CliAssemblyPath -PathType Leaf)) { throw "Build the PowerForge CLI first: $CliAssemblyPath" }
if (-not $WorkspacePath) { $WorkspacePath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/PublicCorpus' }
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
New-Item -ItemType Directory -Path $WorkspacePath -Force | Out-Null
$packageCache = Join-Path $WorkspacePath 'packages'
$extractCache = Join-Path $WorkspacePath 'extract'
$buildCache = Join-Path $WorkspacePath 'build-cache'
New-Item -ItemType Directory -Path $packageCache, $extractCache, $buildCache -Force | Out-Null
$runId = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runRoot = Join-Path $WorkspacePath ('runs/' + $runId)
Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Corpus run' | Out-Null
New-Item -ItemType Directory -Path $runRoot | Out-Null
if (-not $EvidencePath) { $EvidencePath = Join-Path $WorkspacePath ('evidence/' + $runId + '.json') }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if (-not $RuntimeIdentifier) { $RuntimeIdentifier = Get-CurrentRuntimeIdentifier }

$moduleResults = [Collections.Generic.List[object]]::new()
$strictResults = [Collections.Generic.List[object]]::new()
try {
    if (-not $SkipModules) {
        $selectedModules = @($packet.modules)
        if ($ModuleId) { $selectedModules = @($selectedModules | Where-Object { $_.id -in $ModuleId }) }
        foreach ($entry in $selectedModules) {
            Write-Host "[$($entry.id)] acquire, analyze, lock, build, import, invoke"
            try {
                if ($entry.revisionKind -ne 'PSGalleryPackageVersion' -or $entry.revision -ne $entry.version -or $entry.restorePolicy -ne 'IsolatedExactPackage') {
                    throw "Unsupported corpus acquisition contract for $($entry.id)."
                }
                if ($entry.packageUrl -notmatch '^https://www\.powershellgallery\.com/api/v2/package/') { throw "Package URL is outside the allowed gallery endpoint: $($entry.packageUrl)" }
                if ($entry.sha256 -notmatch '^[0-9a-f]{64}$') { throw "Invalid package SHA-256 for $($entry.id)." }
                $packagePath = Get-VerifiedPackage -Entry $entry -PackageCache $packageCache
                $sourceRoot = Expand-VerifiedPackage -Entry $entry -PackagePath $packagePath -ExtractCache $extractCache
                $entryPoint = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $entry.entryPoint) -Label 'Corpus entry point'
                if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) { throw "Corpus entry point does not exist: $entryPoint" }

                $analysis = Invoke-PowerForgeJson -Arguments @('powershell', 'analyze', $entryPoint, '--kind', 'dll', '--mode', $packet.mode, '--framework', $packet.targetFramework, '--resource-mode', $packet.resourceMode) -WorkingDirectory $runRoot
                $lockPath = Join-Path $runRoot ('locks/' + $entry.id + '.lock.json')
                Write-Utf8Json -Path $lockPath -Value $analysis.Json.result.dependencyGraph
                $output = Join-Path $runRoot ('modules/' + $entry.id)
                $build = Invoke-PowerForgeJson -Arguments @('powershell', 'build', $entryPoint, '--kind', 'dll', '--mode', $packet.mode, '--framework', $packet.targetFramework, '--out', $output, '--name', $entry.id, '--resource-mode', $packet.resourceMode, '--dependency-lock', $lockPath, '--cache-directory', $buildCache, '--no-build-cache') -WorkingDirectory $runRoot
                $manifest = $build.Json.result.manifest
                if ($manifest.dependencyGraph.lockSha256 -ne $analysis.Json.result.dependencyGraph.lockSha256) { throw 'Build did not consume the analyzed dependency lock.' }
                $probe = Invoke-CleanModuleProbe -ManifestPath $build.Json.result.artifactPath -ProbeScript $entry.probeScript -WorkingDirectory $runRoot
                $moduleResults.Add([ordered]@{
                    id = $entry.id
                    version = $entry.version
                    scenarioFamily = $entry.scenarioFamily
                    packageSha256 = $entry.sha256
                    dependencyLockSha256 = $manifest.dependencyGraph.lockSha256
                    artifactSha256 = $manifest.artifactSha256
                    artifactBytes = $manifest.artifactSizeBytes
                    cleanImport = $true
                    invocation = $probe.Json.probeSucceeded
                    exportedCommands = $probe.Json.exportedCommands
                    counts = Get-LedgerSummary -Ledger $manifest.unitDispositionLedger
                    durationMilliseconds = [ordered]@{ analyze = $analysis.DurationMilliseconds; build = $build.DurationMilliseconds; probe = $probe.DurationMilliseconds }
                    succeeded = $true
                })
            } catch {
                $moduleResults.Add([ordered]@{ id = $entry.id; version = $entry.version; scenarioFamily = $entry.scenarioFamily; succeeded = $false; error = $_.Exception.Message })
            }
        }
    }

    if (-not $SkipStrictPrograms) {
        if ($RuntimeIdentifier -notin @($packet.strictRuntimeIdentifiers)) { throw "Strict packet RID '$RuntimeIdentifier' is not selected by the fixed packet." }
        $targetContractPath = Join-Path $runRoot ('contracts/strict-' + $RuntimeIdentifier + '.json')
        New-TargetContract -Rid $RuntimeIdentifier -TargetFramework $packet.strictTargetFramework -SemanticProfileId $packet.semanticProfile -Path $targetContractPath
        foreach ($program in @($packet.strictPrograms)) {
            Write-Host "[$($program.id)] analyze, lock, build, execute on $RuntimeIdentifier"
            try {
                $entryPoint = Assert-ContainedPath -Root $PSScriptRoot -Path (Join-Path $PSScriptRoot $program.entryPoint) -Label 'Strict program entry point'
                $analysis = Invoke-PowerForgeJson -Arguments @('powershell', 'analyze', $entryPoint, '--target-contract', $targetContractPath, '--resource-mode', 'Declared') -WorkingDirectory $runRoot
                $lockPath = Join-Path $runRoot ('locks/' + $program.id + '-' + $RuntimeIdentifier + '.lock.json')
                Write-Utf8Json -Path $lockPath -Value $analysis.Json.result.dependencyGraph
                $output = Join-Path $runRoot ('strict/' + $RuntimeIdentifier + '/' + $program.id)
                $build = Invoke-PowerForgeJson -Arguments @('powershell', 'build', $entryPoint, '--out', $output, '--name', $program.id, '--target-contract', $targetContractPath, '--dependency-lock', $lockPath, '--cache-directory', $buildCache, '--no-build-cache') -WorkingDirectory $runRoot
                $manifest = $build.Json.result.manifest
                if ($manifest.requiresPowerShellRuntime -or $manifest.usesPowerShellRuntimeFallback) { throw 'Strict artifact retained a PowerShell runtime requirement.' }
                if ($manifest.dependencyGraph.lockSha256 -ne $analysis.Json.result.dependencyGraph.lockSha256) { throw 'Strict build did not consume the analyzed dependency lock.' }
                $execution = Invoke-OwnedProcess -FileName $build.Json.result.artifactPath -Arguments @() -WorkingDirectory $runRoot -Environment @{} -TimeoutSeconds 120
                if ($execution.ExitCode -ne 0 -or $execution.StandardOutput.Trim() -ne $program.expectedOutput -or $execution.StandardError.Trim()) {
                    throw "Strict execution mismatch. Exit=$($execution.ExitCode); stdout=$($execution.StandardOutput); stderr=$($execution.StandardError)"
                }
                $strictResults.Add([ordered]@{
                    id = $program.id
                    scenarioFamily = $program.scenarioFamily
                    runtimeIdentifier = $RuntimeIdentifier
                    dependencyLockSha256 = $manifest.dependencyGraph.lockSha256
                    artifactSha256 = $manifest.artifactSha256
                    artifactBytes = $manifest.artifactSizeBytes
                    counts = Get-LedgerSummary -Ledger $manifest.unitDispositionLedger
                    expectedOutput = $program.expectedOutput
                    completeStrictProgram = $true
                    durationMilliseconds = [ordered]@{ analyze = $analysis.DurationMilliseconds; build = $build.DurationMilliseconds; execute = $execution.DurationMilliseconds }
                    succeeded = $true
                })
            } catch {
                $strictResults.Add([ordered]@{ id = $program.id; scenarioFamily = $program.scenarioFamily; runtimeIdentifier = $RuntimeIdentifier; succeeded = $false; error = $_.Exception.Message })
            }
        }
    }

    $failedModules = @($moduleResults | Where-Object { -not $_.succeeded }).Count
    $failedStrict = @($strictResults | Where-Object { -not $_.succeeded }).Count
    if (-not (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) { throw "Public corpus baseline does not exist: $BaselinePath" }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json
    $packetSha256 = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $compareModules = -not $SkipModules -and -not $ModuleId
    $compareStrict = -not $SkipStrictPrograms
    $regressions = @(Compare-PublicBaseline -Baseline $baseline -Packet $packet -PacketSha256 $packetSha256 -Modules @($moduleResults) -StrictPrograms @($strictResults) -Rid $RuntimeIdentifier -CompareModules $compareModules -CompareStrict $compareStrict)
    $evidence = [ordered]@{
        schemaVersion = 1
        packetId = $packet.packetId
        packetSha256 = $packetSha256
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        semanticProfile = $packet.semanticProfile
        host = [ordered]@{
            runtimeIdentifier = Get-CurrentRuntimeIdentifier
            operatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
            architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
            powerShell = $PSVersionTable.PSVersion.ToString()
            dotnet = (& dotnet --version)
        }
        modules = @($moduleResults)
        strictPrograms = @($strictResults)
        regressions = $regressions
        baselineScope = [ordered]@{ hybrid = if ($compareModules) { 'Full' } else { 'IdentityOnly' }; strict = if ($compareStrict) { 'Full' } else { 'IdentityOnly' } }
        summary = [ordered]@{
            modulesPassed = @($moduleResults | Where-Object succeeded).Count
            modulesTotal = $moduleResults.Count
            strictProgramsPassed = @($strictResults | Where-Object succeeded).Count
            strictProgramsTotal = $strictResults.Count
            failures = $failedModules + $failedStrict
            regressions = $regressions.Count
        }
    }
    Write-Utf8Json -Path $EvidencePath -Value $evidence
    Write-Host "Evidence: $EvidencePath"
    Write-Host "Modules: $($evidence.summary.modulesPassed)/$($evidence.summary.modulesTotal); Strict programs: $($evidence.summary.strictProgramsPassed)/$($evidence.summary.strictProgramsTotal)"
    if ($evidence.summary.failures -gt 0 -or $evidence.summary.regressions -gt 0) { exit 1 }
} finally {
    if (-not $KeepRunArtifacts -and (Test-Path -LiteralPath $runRoot)) {
        Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Corpus run cleanup' | Out-Null
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
