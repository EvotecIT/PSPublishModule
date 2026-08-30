[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'public-corpus.net8.json'),
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

function Get-PathComparison {
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        return [System.StringComparison]::OrdinalIgnoreCase
    }
    return [System.StringComparison]::Ordinal
}

function Assert-ContainedPath {
    param([string] $Root, [string] $Path, [string] $Label)

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $pathFull = [IO.Path]::GetFullPath($Path)
    $prefix = $rootFull + [IO.Path]::DirectorySeparatorChar
    if (-not $pathFull.StartsWith($prefix, (Get-PathComparison))) {
        throw "$Label escapes its declared root: $pathFull"
    }
    return $pathFull
}

function Write-Utf8Json {
    param([string] $Path, [object] $Value, [int] $Depth = 100)

    $parent = Split-Path -Parent $Path
    if ($parent) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
    $json = $Value | ConvertTo-Json -Depth $Depth
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

function Invoke-OwnedProcess {
    param(
        [string] $FileName,
        [string[]] $Arguments,
        [string] $WorkingDirectory,
        [hashtable] $Environment,
        [int] $TimeoutSeconds = 600
    )

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FileName
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = $WorkingDirectory
    $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { [void] $start.ArgumentList.Add($argument) }
    foreach ($key in @($Environment.Keys)) { $start.Environment[$key] = [string] $Environment[$key] }

    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try { $process.Kill($true) } catch { }
            throw "Owned process '$FileName' exceeded $TimeoutSeconds seconds."
        }
        $clock.Stop()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = $stdout.GetAwaiter().GetResult()
            StandardError = $stderr.GetAwaiter().GetResult()
            DurationMilliseconds = $clock.ElapsedMilliseconds
        }
    } finally {
        $process.Dispose()
    }
}

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

    $packagePath = Join-Path $PackageCache ($Entry.sha256 + '.nupkg')
    if (-not (Test-Path -LiteralPath $packagePath)) {
        if ($Offline) { throw "Offline package cache miss for $($Entry.id) $($Entry.version)." }
        $temporaryPath = $packagePath + '.' + [guid]::NewGuid().ToString('N') + '.download'
        Assert-ContainedPath -Root $PackageCache -Path $temporaryPath -Label 'Package download' | Out-Null
        try {
            Invoke-WebRequest -Uri $Entry.packageUrl -OutFile $temporaryPath
            $actual = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actual -ne $Entry.sha256) {
                throw "Package hash mismatch for $($Entry.id) $($Entry.version): expected $($Entry.sha256), received $actual."
            }
            Move-Item -LiteralPath $temporaryPath -Destination $packagePath
        } finally {
            if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
        }
    }
    $cachedHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($cachedHash -ne $Entry.sha256) {
        throw "Cached package hash mismatch for $($Entry.id) $($Entry.version): $packagePath"
    }
    return $packagePath
}

function Expand-VerifiedPackage {
    param([object] $Entry, [string] $PackagePath, [string] $ExtractCache)

    $target = Join-Path $ExtractCache $Entry.sha256
    $marker = Join-Path $target '.powerforge-corpus-package.json'
    if (Test-Path -LiteralPath $marker) {
        $record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($record.id -ne $Entry.id -or $record.version -ne $Entry.version -or $record.sha256 -ne $Entry.sha256) {
            throw "Extracted package marker does not match the requested identity: $target"
        }
        return $target
    }
    if (Test-Path -LiteralPath $target) {
        throw "Unmarked extraction directory already exists and will not be trusted: $target"
    }

    $staging = Join-Path $ExtractCache ($Entry.sha256 + '.' + [guid]::NewGuid().ToString('N') + '.extracting')
    Assert-ContainedPath -Root $ExtractCache -Path $staging -Label 'Package extraction' | Out-Null
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
        try {
            $destinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($archiveEntry in $archive.Entries) {
                $unixType = (($archiveEntry.ExternalAttributes -shr 16) -band 0xF000)
                if ($unixType -eq 0xA000) { throw "Package contains a symbolic link: $($archiveEntry.FullName)" }
                $relative = $archiveEntry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
                if ([IO.Path]::IsPathRooted($relative)) { throw "Package contains a rooted entry: $relative" }
                $destination = Assert-ContainedPath -Root $staging -Path (Join-Path $staging $relative) -Label 'Package entry'
                if (-not $destinations.Add($destination)) { throw "Package contains a portable path collision: $relative" }
                if ([string]::IsNullOrEmpty($archiveEntry.Name)) {
                    New-Item -ItemType Directory -Path $destination -Force | Out-Null
                    continue
                }
                New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
                $source = $archiveEntry.Open()
                $destinationStream = [IO.File]::Create($destination)
                try { $source.CopyTo($destinationStream) }
                finally { $destinationStream.Dispose(); $source.Dispose() }
            }
        } finally {
            $archive.Dispose()
        }
        Write-Utf8Json -Path (Join-Path $staging '.powerforge-corpus-package.json') -Value ([ordered]@{
            schemaVersion = 1
            id = $Entry.id
            version = $Entry.version
            sha256 = $Entry.sha256
        })
        Move-Item -LiteralPath $staging -Destination $target
    } catch {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
        throw
    }
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
    param([string] $Rid, [string] $TargetFramework, [string] $Path)

    $parts = $Rid.Split('-')
    Write-Utf8Json -Path $Path -Value ([ordered]@{
        schemaVersion = 2
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
        contractSha256 = ''
    })
}

function Get-CurrentRuntimeIdentifier {
    $architecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { return "win-$architecture" }
    if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { return "osx-$architecture" }
    return "linux-$architecture"
}

$PacketPath = [IO.Path]::GetFullPath($PacketPath)
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
        New-TargetContract -Rid $RuntimeIdentifier -TargetFramework $packet.strictTargetFramework -Path $targetContractPath
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
    $evidence = [ordered]@{
        schemaVersion = 1
        packetId = $packet.packetId
        packetSha256 = (Get-FileHash -LiteralPath $PacketPath -Algorithm SHA256).Hash.ToLowerInvariant()
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
        summary = [ordered]@{
            modulesPassed = @($moduleResults | Where-Object succeeded).Count
            modulesTotal = $moduleResults.Count
            strictProgramsPassed = @($strictResults | Where-Object succeeded).Count
            strictProgramsTotal = $strictResults.Count
            failures = $failedModules + $failedStrict
        }
    }
    Write-Utf8Json -Path $EvidencePath -Value $evidence
    Write-Host "Evidence: $EvidencePath"
    Write-Host "Modules: $($evidence.summary.modulesPassed)/$($evidence.summary.modulesTotal); Strict programs: $($evidence.summary.strictProgramsPassed)/$($evidence.summary.strictProgramsTotal)"
    if ($evidence.summary.failures -gt 0) { exit 1 }
} finally {
    if (-not $KeepRunArtifacts -and (Test-Path -LiteralPath $runRoot)) {
        Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Corpus run cleanup' | Out-Null
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
