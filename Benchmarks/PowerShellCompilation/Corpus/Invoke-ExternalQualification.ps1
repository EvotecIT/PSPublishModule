[CmdletBinding()]
param(
    [string] $PacketPath = (Join-Path $PSScriptRoot 'external-assessment.net10.json'),
    [string] $CliAssemblyPath,
    [string] $WorkspacePath,
    [string] $EvidencePath,
    [string[]] $WorkloadId,
    [switch] $Offline,
    [switch] $AllowExternalExecution
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')

if (-not $AllowExternalExecution) {
    throw 'External qualification imports and invokes reviewed third-party source. Pass -AllowExternalExecution to authorize this local execution lane.'
}

function Invoke-CliJson {
    param([string[]] $Arguments, [string] $WorkingDirectory)

    $run = Invoke-OwnedProcess -FileName 'dotnet' -Arguments (@($CliAssemblyPath) + $Arguments) -WorkingDirectory $WorkingDirectory
    try { $json = $run.StandardOutput | ConvertFrom-Json }
    catch { throw "PowerForge CLI did not return JSON. Exit=$($run.ExitCode); stderr=$($run.StandardError)" }
    if ($run.ExitCode -ne 0 -or -not $json.success) {
        $message = if ($json.error) { [string] $json.error } else { [string] $run.StandardError }
        throw "PowerForge CLI failed with exit $($run.ExitCode): $message"
    }
    return $json.result
}

function Quote-PowerShellLiteral {
    param([string] $Value)
    return "'" + $Value.Replace("'", "''") + "'"
}

$PacketPath = [IO.Path]::GetFullPath($PacketPath)
$packet = Get-Content -LiteralPath $PacketPath -Raw | ConvertFrom-Json
if ($packet.schemaVersion -ne 1) { throw "Unsupported external-assessment schema $($packet.schemaVersion)." }
$qualified = @($packet.workloads | Where-Object { $null -ne $_.PSObject.Properties['qualification'] })
if ($WorkloadId) { $qualified = @($qualified | Where-Object { $_.id -in $WorkloadId }) }
$unknown = @($WorkloadId | Where-Object { $_ -notin @($packet.workloads.id) } | Sort-Object -Unique)
if ($unknown.Count -gt 0) { throw "Unknown assessment workload id(s): $($unknown -join ', ')" }
if ($qualified.Count -eq 0) { throw 'No selected workload declares an external qualification contract.' }

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
New-Item -ItemType Directory -Path $runRoot | Out-Null
if (-not $EvidencePath) { $EvidencePath = Join-Path $WorkspacePath ('evidence/' + $runId + '.json') }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)

try {
    $assessmentEvidence = Join-Path $runRoot 'assessment.json'
    $assessmentArguments = @{
        PacketPath = $PacketPath
        CliAssemblyPath = $CliAssemblyPath
        WorkspacePath = $WorkspacePath
        EvidencePath = $assessmentEvidence
        WorkloadId = @($qualified.id)
        Offline = $Offline
    }
    & (Join-Path $PSScriptRoot 'Invoke-ExternalAssessment.ps1') @assessmentArguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The non-executing assessment prerequisite exited with $LASTEXITCODE." }

    $results = [Collections.Generic.List[object]]::new()
    foreach ($entry in $qualified) {
        Write-Information "[$($entry.id)] build, import, and invoke reviewed Hybrid command" -InformationAction Continue
        try {
            if ($entry.qualification.kind -ne 'HybridModuleCommand') { throw "Unsupported qualification kind '$($entry.qualification.kind)'." }
            if ([string]::IsNullOrWhiteSpace($entry.qualification.unitName) -or [string]::IsNullOrWhiteSpace($entry.qualification.probeScript)) {
                throw 'HybridModuleCommand qualification requires unitName and probeScript.'
            }
            $importSurface = if ($null -ne $entry.qualification.PSObject.Properties['importSurface']) {
                [string] $entry.qualification.importSurface
            } else {
                'Manifest'
            }
            if ($importSurface -notin @('Manifest', 'RootModuleDirect')) {
                throw "Unsupported qualification import surface '$importSurface'."
            }
            $sourceRoot = Join-Path $WorkspacePath ('extract/' + $entry.acquisition.sha256)
            $qualificationEntryPoint = if ($null -ne $entry.qualification.PSObject.Properties['entryPoint']) {
                [string] $entry.qualification.entryPoint
            } else {
                [string] $entry.entryPoint
            }
            $sourcePath = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $qualificationEntryPoint) -Label 'Qualification entry point'
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
            if ($importSurface -eq 'RootModuleDirect') {
                $artifactRoot = Split-Path -Parent $compiledImportPath
                $compiledManifest = Import-PowerShellDataFile -LiteralPath $compiledImportPath
                if ([string]::IsNullOrWhiteSpace($compiledManifest.RootModule)) { throw 'Generated module manifest declares no root module.' }
                $compiledImportPath = Assert-ContainedPath -Root $artifactRoot -Path (Join-Path $artifactRoot $compiledManifest.RootModule) -Label 'Qualification generated root module'
                if (-not (Test-Path -LiteralPath $compiledImportPath -PathType Leaf)) { throw "Generated root module does not exist: $compiledImportPath" }
            }
            $originalCommand = 'Import-Module -Name ' + (Quote-PowerShellLiteral $sourcePath) + ' -Force -ErrorAction Stop; ' + $entry.qualification.probeScript
            $compiledCommand = 'Import-Module -Name ' + (Quote-PowerShellLiteral $compiledImportPath) + ' -Force -ErrorAction Stop; ' + $entry.qualification.probeScript
            $original = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoProfile', '-NonInteractive', '-Command', $originalCommand) -WorkingDirectory $runRoot -TimeoutSeconds 120
            $compiled = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoProfile', '-NonInteractive', '-Command', $compiledCommand) -WorkingDirectory $runRoot -TimeoutSeconds 120
            if ($original.ExitCode -ne 0 -or $compiled.ExitCode -ne 0 -or
                $original.StandardOutput -ne $compiled.StandardOutput -or $original.StandardError -ne $compiled.StandardError) {
                throw "Original and compiled qualification invocations differ. OriginalExit=$($original.ExitCode); CompiledExit=$($compiled.ExitCode)."
            }
            $results.Add([ordered]@{
                id = $entry.id
                unitName = $entry.qualification.unitName
                importSurface = $importSurface
                emittedClrMethod = $true
                runtimeRouted = [int] $ledgerEntry[0].runtimeCommandRegions -gt 0 -or [bool] $ledgerEntry[0].retainedHostedSource
                compiledMethods = [int] $build.manifest.compiledMethods
                outputSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($compiled.StandardOutput))).ToLowerInvariant()
                invocationParity = $true
                completeWorkloadExecution = $false
                succeeded = $true
            })
        } catch {
            $results.Add([ordered]@{ id = $entry.id; succeeded = $false; error = $_.Exception.Message })
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
        }
        interpretation = 'A qualification proves invocation parity for the named emitted command only. It is not proof that the complete external workload compiled or executed without PowerShell.'
    }
    Write-Utf8Json -Path $EvidencePath -Value $evidence
    Write-Information "Evidence: $EvidencePath" -InformationAction Continue
    Write-Information "Qualified emitted commands: $($evidence.summary.emittedCommandsInvoked)/$($evidence.summary.total)" -InformationAction Continue
    if ($evidence.summary.passed -ne $evidence.summary.total) { exit 1 }
} finally {
    if (Test-Path -LiteralPath $runRoot) {
        Assert-ContainedPath -Root $WorkspacePath -Path $runRoot -Label 'Qualification run cleanup' | Out-Null
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    }
}
