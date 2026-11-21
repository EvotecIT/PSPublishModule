function Get-WorkflowEffectSnapshot {
    param([string] $Root)

    $files = [ordered]@{}
    foreach ($file in (Get-ChildItem -LiteralPath $Root -File -Recurse | Sort-Object FullName)) {
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A scenario effect must not be a reparse point.' }
        $relativePath = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        $files[$relativePath] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $files
}

function ConvertTo-WorkflowObservationText {
    param(
        [AllowEmptyString()][string] $Text,
        [string] $CaseRoot,
        [string] $AuthoredPath,
        [string] $EntryPath,
        [string] $WorkloadId
    )

    $value = [regex]::Replace($Text, '\x1b\[[0-9;?]*[ -/]*[@-~]', '')
    $value = $value.Replace([string][char]13 + [char]10, [string][char]10)
    foreach ($path in @($AuthoredPath, $EntryPath)) {
        if ($path) { $value = $value.Replace($path, '<entry>') }
    }
    $value = $value.Replace($CaseRoot, '<fixture>')
    return ConvertTo-UnfamiliarScenarioText $value $WorkloadId
}

function Invoke-WorkflowCase {
    param($Entry, $Case, [string] $EntryPath, [string] $AuthoredPath, [string] $HostPath, [bool] $Original)

    $root = [string] $Case.workingDirectory
    $before = Get-WorkflowEffectSnapshot $root
    if ($Entry.workloadKind -eq 'Module' -or $Original) {
        $executable = $HostPath
        $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass')
        if ($Entry.workloadKind -eq 'Module') {
            $arguments += @('-File', $Case.observerPath, '-ArtifactPath', $EntryPath)
        } else {
            $arguments += @('-File', $EntryPath)
        }
    } else {
        $executable = $EntryPath
        $arguments = @()
    }
    $arguments += @($Case.arguments)
    $process = Invoke-OwnedProcess -FileName $executable -Arguments $arguments -WorkingDirectory $root -TimeoutSeconds 75 -Environment @{
        NO_COLOR = '1'
        POWERSHELL_TELEMETRY_OPTOUT = '1'
        TEMP = $root
        TMP = $root
    }
    $after = Get-WorkflowEffectSnapshot $root
    $effects = [ordered]@{}
    foreach ($key in $after.Keys) {
        if (-not $before.Contains($key) -or $before[$key] -ne $after[$key]) {
            $effects[$key] = $after[$key]
        }
    }
    $outcome = [ordered]@{
        id = $Case.id
        exitCode = $process.ExitCode
        stdout = ConvertTo-WorkflowObservationText $process.StandardOutput $root $AuthoredPath $EntryPath $Entry.id
        stderr = ConvertTo-WorkflowObservationText $process.StandardError $root $AuthoredPath $EntryPath $Entry.id
        changedFiles = $effects
        removedFiles = @($before.Keys | Where-Object { -not $after.Contains($_) })
    }
    Write-Utf8Json ($root + '.process.json') $process
    return $outcome
}

function Invoke-WorkflowCases {
    param($Entry, [string] $CaseRoot, [string] $EntryPath, [string] $AuthoredPath,
        [string] $HostPath, [string] $AcquiredRoot, [bool] $Original)

    foreach ($fixture in @(New-UnfamiliarCases $Entry $CaseRoot $AcquiredRoot)) {
        $caseEntry = if ($Original) { Initialize-UnfamiliarOriginalCase $Entry $fixture $EntryPath $AcquiredRoot } else { $EntryPath }
        Invoke-WorkflowCase $Entry $fixture $caseEntry $AuthoredPath $HostPath $Original
    }
}

function Invoke-WorkflowArtifactCases {
    param($Entry, [string] $CaseRoot, [string] $EntryPath, [string] $AuthoredPath,
        [string] $HostPath, [string] $AcquiredRoot, [string] $StagedRoot)

    $withdrawn = $StagedRoot + '.withdrawn'
    Assert-WorkflowPathWithoutLinks $StagedRoot
    if (Test-Path -LiteralPath $withdrawn) { throw 'Compiler inputs are already withdrawn.' }
    Move-Item -LiteralPath $StagedRoot -Destination $withdrawn
    try {
        Invoke-WorkflowCases $Entry $CaseRoot $EntryPath $AuthoredPath $HostPath $AcquiredRoot $false
    } finally {
        if (Test-Path -LiteralPath $StagedRoot) { throw 'The artifact recreated its compiler input directory.' }
        Move-Item -LiteralPath $withdrawn -Destination $StagedRoot
    }
}

function Get-WorkflowArtifactPrimary {
    param($Entry, $Target, [string] $OutputRoot, [string] $BuiltPath)

    $built = Assert-ContainedPath -Root $OutputRoot -Path $BuiltPath -Label 'Baseline artifact output'
    if ($Entry.workloadKind -eq 'Module' -and [IO.Path]::GetExtension($built) -ne '.psd1') {
        $manifest = [IO.Path]::ChangeExtension($built, '.psd1')
        if (Test-Path -LiteralPath $manifest -PathType Leaf) { return $manifest }
        throw "Build did not return the generated manifest for target '$($Target.name)'."
    }
    return $built
}

function Invoke-WorkflowBaselineLane {
    param($Entry, [string] $TargetFramework, [string] $AcquiredRoot, [string] $LaneRoot, [string] $HostPath)

    $sourceRoot = Join-Path $LaneRoot 'source'
    $projectPath = Join-Path $LaneRoot 'powerforge.psproject.json'
    $lane = [ordered]@{
        id = $Entry.id
        target = $TargetFramework
        status = 'stage-failed'
        stage = 'acquire'
        observations = [ordered]@{}
    }
    $environment = @{
        PATH = (Split-Path -Parent $HostPath) + [IO.Path]::PathSeparator + $env:PATH
        POWERSHELL_TELEMETRY_OPTOUT = '1'
    }
    try {
        $inputs = Get-CorpusProperty $Entry 'sourceInputs'
        if ($null -ne $inputs) {
            Copy-WorkflowSourceInputs $Entry $AcquiredRoot $sourceRoot
        } else {
            Assert-WorkflowPathWithoutLinks $AcquiredRoot
            Copy-Item -LiteralPath $AcquiredRoot -Destination $sourceRoot -Recurse
        }
        $source = Assert-ContainedPath -Root $sourceRoot -Path (Join-Path $sourceRoot $Entry.entryPoint) -Label 'Baseline entrypoint'
        $lane.stage = 'source'
        $lane.observations.source = @(Invoke-WorkflowCases $Entry (Join-Path $LaneRoot 'source-cases') $source $source $HostPath $AcquiredRoot $true)
        $positive = $lane.observations.source[0]
        if ($lane.observations.source.Count -ne 2 -or $positive.exitCode -ne 0 -or
            ($positive.stdout.Length -eq 0 -and $positive.changedFiles.Count -eq 0)) {
            throw 'The source host did not produce the declared success observation.'
        }
        $module = $Entry.workloadKind -eq 'Module'
        $kind = if ($module) { 'dll' } else { 'exe' }
        $profile = if ($TargetFramework -eq 'net472') { 'PowerForge.Oracle.WindowsPowerShell/5.1' } else { 'PowerForge.Oracle.PowerShell/7.6' }
        $init = @('powershell', 'project', 'init', $source, '--project', $projectPath, '--name', $Entry.id,
            '--kind', $kind, '--mode', 'Hybrid', '--framework', $TargetFramework, '--semantic-profile', $profile, '--output', 'json')
        if (-not $module) { $init += @('--rid', 'win-x64', '--no-single-file') }
        $lane.stage = 'init'
        $null = Invoke-WorkflowCliOperation $init $LaneRoot (Join-Path $LaneRoot 'init.json') $environment
        $project = Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json -AsHashtable
        $target = $project.artifacts[0]
        $target.artifactName = $Entry.id
        $project.resources.mode = if ($module) { 'CompleteModule' } else { 'Declared' }
        $resources = Get-CorpusProperty $Entry 'resources'
        if ($null -ne $resources) { $project.resources = $resources }
        if (-not $module) {
            $testCases = @(New-UnfamiliarCases $Entry (Join-Path $LaneRoot 'project-test') $AcquiredRoot)
            $target.testArguments = @($testCases[0].arguments)
            $target.testWorkingDirectory = [IO.Path]::GetRelativePath($LaneRoot, $testCases[0].workingDirectory).Replace('\', '/')
        }
        Write-Utf8Json $projectPath $project
        foreach ($stage in @('analyze', 'lock', 'restore', 'build')) {
            $lane.stage = $stage
            $build = Invoke-WorkflowCliOperation @('powershell','project',$stage,$projectPath,'--output','json') $LaneRoot (Join-Path $LaneRoot ($stage + '.json')) $environment
        }
        $outputRoot = Join-Path $LaneRoot $target.outputDirectory
        $built = Get-WorkflowArtifactPrimary $Entry $target $outputRoot $build.targets[0].path
        $receiptPath = Join-Path $LaneRoot ('.powerforge/build/' + $target.name + '.json')
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        $artifactHash = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($artifactHash -ne $receipt.manifest.artifactSha256) {
            throw 'The generated entrypoint differs from the authenticated build receipt.'
        }
        $lane['artifactSha256'] = $artifactHash
        $lane['emittedBodies'] = @($receipt.manifest.unitDispositionLedger.entries | Where-Object {
            $_.emittedClrMethod -and -not $_.retainedHostedSource
        } | Select-Object name,relativePath,unitId)
        $lane.stage = 'built-observation'
        $lane.observations.built = @(Invoke-WorkflowArtifactCases $Entry (Join-Path $LaneRoot 'built-cases') $built $source $HostPath $AcquiredRoot $sourceRoot)
        foreach ($stage in @('test', 'pack', 'install')) {
            $lane.stage = $stage
            $install = Invoke-WorkflowCliOperation @('powershell','project',$stage,$projectPath,'--output','json') $LaneRoot (Join-Path $LaneRoot ($stage + '.json')) $environment
        }
        $installedRoot = Join-Path $install.targets[0].path 'artifact'
        $installed = Assert-ContainedPath -Root $installedRoot -Path (Join-Path $installedRoot ([IO.Path]::GetRelativePath($outputRoot, $built))) -Label 'Installed baseline artifact'
        if ($artifactHash -ne (Get-FileHash -LiteralPath $installed -Algorithm SHA256).Hash) {
            throw 'The installed entrypoint bytes differ from the authenticated built entrypoint.'
        }
        $lane.stage = 'installed-observation'
        $lane.observations.installed = @(Invoke-WorkflowArtifactCases $Entry (Join-Path $LaneRoot 'installed-cases') $installed $source $HostPath $AcquiredRoot $sourceRoot)
        $reference = ConvertTo-Json -InputObject $lane.observations.source -Depth 15 -Compress
        $builtValue = ConvertTo-Json -InputObject $lane.observations.built -Depth 15 -Compress
        $installedValue = ConvertTo-Json -InputObject $lane.observations.installed -Depth 15 -Compress
        $lane.status = if ($reference -ceq $builtValue -and $reference -ceq $installedValue) { 'parity' } else { 'semantic-difference' }
        $lane.stage = 'complete'
    } catch {
        $lane['error'] = $_.Exception.Message
        $ownedProcess = Get-CorpusOwnedProcessFailureEvidence $_.Exception
        if ($null -ne $ownedProcess) { $lane['ownedProcessFailure'] = $ownedProcess }
    }
    Write-Utf8Json (Join-Path $LaneRoot 'lane.json') $lane
    return $lane
}
