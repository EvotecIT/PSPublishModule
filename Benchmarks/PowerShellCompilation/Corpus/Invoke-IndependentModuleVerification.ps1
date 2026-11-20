[CmdletBinding()]
param(
    [string] $CliAssemblyPath,
    [string] $WorkspacePath,
    [string] $PayloadCachePath,
    [string] $EvidencePath,
    [ValidateSet('Plaster', 'PowerShellHumanizer', 'PSIni', 'PSFolderSize')]
    [string[]] $WorkloadId = @('Plaster', 'PowerShellHumanizer', 'PSIni', 'PSFolderSize'),
    [switch] $Offline,
    [switch] $KeepRunArtifacts
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot 'Corpus.Assessment.Acquisition.ps1')

function Get-HostEvidence {
    param([string] $Executable, [string] $WorkingDirectory)

    $identityCommand = '$env:PSModulePath = [IO.Path]::Combine($PSHOME, ''Modules''); ' +
        '[Console]::Write(([ordered]@{version=$PSVersionTable.PSVersion.ToString();edition=$PSVersionTable.PSEdition}| ConvertTo-Json -Compress))'
    $identity = Invoke-OwnedProcess -FileName $Executable -Arguments @('-NoProfile', '-NonInteractive', '-Command', $identityCommand) -WorkingDirectory $WorkingDirectory -TimeoutSeconds 45
    if ($identity.ExitCode -ne 0 -or $identity.StandardError) { throw 'Cannot identify the source PowerShell host.' }
    $hostIdentity = $identity.StandardOutput | ConvertFrom-Json
    return [ordered]@{
        version = $hostIdentity.version; edition = $hostIdentity.edition
        executableSha256 = (Get-FileHash -LiteralPath $Executable -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Invoke-WorkflowObservation {
    param([string] $Executable, [string] $ModulePath, [string] $Id, [string] $DataDirectory)

    $null = New-Item -ItemType Directory -Path $DataDirectory
    $observer = Join-Path $PSScriptRoot 'Observe-IndependentModuleWorkflow.ps1'
    $arguments = @('-NoProfile', '-NonInteractive', '-File', $observer, $ModulePath, $Id, $DataDirectory)
    $observation = Invoke-OwnedProcess -FileName $Executable -Arguments $arguments -WorkingDirectory $DataDirectory -TimeoutSeconds 120
    if ($observation.ExitCode -ne 0 -or $observation.StandardError) {
        throw "Module observation failed with exit $($observation.ExitCode). $($observation.StandardError)"
    }
    return $observation.StandardOutput
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'The independent module matrix requires Windows PowerShell 5.1 and the pinned Windows PowerShell 7 host.'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $CliAssemblyPath) { $CliAssemblyPath = Join-Path $repository 'PowerForge.Cli/bin/Release/net10.0/PowerForge.Cli.dll' }
$CliAssemblyPath = (Resolve-Path -LiteralPath $CliAssemblyPath).Path
if (-not $WorkspacePath) { $WorkspacePath = Join-Path ([IO.Path]::GetTempPath()) 'PowerForge/IndependentModuleVerification' }
$WorkspacePath = [IO.Path]::GetFullPath($WorkspacePath)
if (-not $PayloadCachePath) { $PayloadCachePath = Join-Path $WorkspacePath 'payloads' }
$PayloadCachePath = [IO.Path]::GetFullPath($PayloadCachePath)
$null = New-Item -ItemType Directory -Path $WorkspacePath -Force
$runId = [guid]::NewGuid().ToString('N')
$run = Assert-ContainedPath -Root $WorkspacePath -Path (Join-Path $WorkspacePath $runId) -Label 'Independent module run'
if (-not $EvidencePath) { $EvidencePath = Join-Path $WorkspacePath "$runId.json" }
$EvidencePath = [IO.Path]::GetFullPath($EvidencePath)
if ($EvidencePath.Equals($run, (Get-PathComparison)) -or
    $EvidencePath.StartsWith($run + [IO.Path]::DirectorySeparatorChar, (Get-PathComparison)) -or
    (Test-Path -LiteralPath $EvidencePath)) { throw 'Use a new evidence path outside the temporary run directory.' }

$packet = Read-AssessmentPacket (Join-Path $PSScriptRoot 'independent-readiness.net10.json')
$pinsPath = Join-Path $repository 'PowerForge/Resources/PowerShellCompilation/SemanticOracle/host-artifact-pins.json'
$pins = Get-Content -LiteralPath $pinsPath -Raw | ConvertFrom-Json
$pin = ($pins.pins | Where-Object profileId -CEQ $packet.Packet.semanticProfile).hostArtifact
if (-not $env:POWERFORGE_PWSH76_PATH) { throw 'Set POWERFORGE_PWSH76_PATH to the pinned pwsh executable.' }
$targets = @(
    @{ framework='net10.0'; profile=$packet.Packet.semanticProfile; path=(Resolve-Path -LiteralPath $env:POWERFORGE_PWSH76_PATH).Path },
    @{ framework='net472'; profile='PowerForge.Oracle.WindowsPowerShell/5.1'; path=(Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') }
)
foreach ($target in $targets) {
    $target.identity = Get-HostEvidence -Executable $target.path -WorkingDirectory $WorkspacePath
}
if ($targets[0].identity.version -cne $pin.hostVersion -or $targets[0].identity.executableSha256 -cne $pin.executableSha256) {
    throw 'The configured PowerShell 7 executable does not match the compiler host pin.'
}
if ($targets[1].identity.edition -cne 'Desktop' -or $targets[1].identity.version -notmatch '^5\.1\.') {
    throw 'The installed Windows PowerShell 5.1 profile is required.'
}

$entries = @($packet.Packet.workloads | Where-Object id -In $WorkloadId)
if ($entries.Count -eq 0 -or $entries.Count -ne @($WorkloadId | Sort-Object -Unique).Count) {
    throw 'Select at least one workflow from the pinned packet.'
}
$expectedMethods = @{
    Plaster = @('Test-PlasterManifest', 'Invoke-Plaster')
    PowerShellHumanizer = @('ConvertFrom-RomanNumeral', 'ConvertTo-RomanNumeral')
    PSFolderSize = @('Get-FolderSize')
    PSIni = @('Export-Ini')
}
$cliSha256 = (Get-FileHash -LiteralPath $CliAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
$results = [Collections.Generic.List[object]]::new()
$total = $entries.Count * $targets.Count
$runError = $null
try {
    $null = New-Item -ItemType Directory -Path $run
    foreach ($entry in $entries) {
        $payload = Get-VerifiedPayload -Entry $entry -PayloadCache $PayloadCachePath -OfflineMode:$Offline
        $extracted = Get-AssessmentSourceRoot -Entry $entry -PayloadPath $payload -ExtractCache (Join-Path $run 'source')
        $sources = Assert-ContainedPath -Root $run -Path (Resolve-Path -LiteralPath $extracted).Path -Label 'Independent module source'
        $withdrawn = Assert-ContainedPath -Root $run -Path "$sources.withdrawn" -Label 'Withdrawn module source'
        $source = Assert-ContainedPath -Root $sources -Path (Join-Path $sources $entry.entryPoint) -Label 'Independent module entry point'
        foreach ($target in $targets) {
            $scope = (New-Item -ItemType Directory -Path (Join-Path $run "$($entry.id)/$($target.framework)")).FullName
            $record = [ordered]@{
                id=$entry.id; version=$entry.revision; payloadSha256=$entry.acquisition.sha256
                framework=$target.framework; profile=$target.profile; host=$target.identity; matched=$false
            }
            Write-Host "[$($entry.id) $($target.framework)] original, reviewed build, and generated module"
            try {
                $original = Invoke-WorkflowObservation $target.path $source $entry.id (Join-Path $scope 'original-data')
                $review = Join-Path $scope 'review.json'
                $options = @('--kind','dll','--mode','Hybrid','--framework',$target.framework,'--semantic-profile',$target.profile,
                    '--resource-mode','CompleteModule','--out',(Join-Path $scope 'artifact'),'--output','json')
                $check = Invoke-OwnedProcess -FileName 'dotnet' -Arguments (@($CliAssemblyPath,'powershell','check',$source) +
                    $options + @('--write-review',$review)) -WorkingDirectory $scope
                $null = ConvertFrom-CorpusCliProcess $check -Label 'PowerForge Check'
                $build = Invoke-OwnedProcess -FileName 'dotnet' -Arguments (@($CliAssemblyPath,'powershell','build',$source) +
                    $options + @('--reviewed-check',$review,'--no-build-cache')) -WorkingDirectory $scope
                $artifact = (ConvertFrom-CorpusCliProcess $build -Label 'PowerForge reviewed build').result
                $record.emittedUnits = $artifact.manifest.emittedUnits
                $record.analyzedUnits = $artifact.manifest.analyzedUnits
                if (-not $artifact.succeeded -or $record.emittedUnits -le 0) { throw 'No emitted module artifact was built.' }
                $record.emittedCommands = @($expectedMethods[$entry.id] | ForEach-Object {
                    $name = $_
                    $units = @($artifact.manifest.unitDispositionLedger.entries | Where-Object name -EQ $name)
                    if ($units.Count -ne 1 -or -not $units[0].emittedClrMethod -or $units[0].retainedHostedSource) {
                        throw "Workflow command '$name' has no complete emitted method."
                    }
                    $name
                })
                Move-Item -LiteralPath $sources -Destination $withdrawn
                try {
                    $generated = Invoke-WorkflowObservation $target.path $artifact.artifactPath $entry.id (Join-Path $scope 'generated-data')
                } finally {
                    Move-Item -LiteralPath $withdrawn -Destination $sources
                }
                $record.original = $original | ConvertFrom-Json
                $record.generated = $generated | ConvertFrom-Json
                if ($original -cne $generated) { throw 'Original and generated workflow results differ.' }
                $record.matched = $true
            } catch {
                $record.error = $_.Exception.Message
            }
            $results.Add($record)
        }
    }
    foreach ($target in $targets) {
        if ((Get-FileHash -LiteralPath $target.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $target.identity.executableSha256) {
            throw 'A source PowerShell host changed during the module matrix.'
        }
    }
    if ((Get-FileHash -LiteralPath $CliAssemblyPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $cliSha256) {
        throw 'The PowerForge CLI changed during the module matrix.'
    }
} catch {
    $runError = $_.Exception.Message
    throw
} finally {
    try {
        $passed = @($results | Where-Object matched).Count
        Write-Utf8Json -Path $EvidencePath -Value ([ordered]@{
            schemaVersion=1; packetSha256=$packet.Sha256; cliSha256=$cliSha256
            passed=$passed; attempted=$results.Count; total=$total; runError=$runError; comparisons=@($results.ToArray())
        })
        Write-Host "$passed/$total comparisons passed. Evidence $EvidencePath"
    } finally {
        if (-not $KeepRunArtifacts -and (Test-Path -LiteralPath $run -PathType Container)) {
            $cleanup = Assert-ContainedPath -Root $WorkspacePath -Path (Resolve-Path -LiteralPath $run).Path -Label 'Independent module cleanup'
            Remove-Item -LiteralPath $cleanup -Recurse
        }
    }
}
if ($passed -ne $total) { throw 'Independent module workflow verification failed.' }
