# Whole-artifact acceptance checks and orchestration over the canonical project workflow.
# Observers are reviewed test code. Their execution reports are evidence, not instrumentation
# that independently proves which compiler-generated instruction ran.
. (Join-Path $PSScriptRoot 'Corpus.Qualification.Metadata.ps1')

function Get-WorkflowProperty {
    param($Value, [string] $Name, $Default = $null)
    if ($Value -is [Collections.IDictionary]) {
        if ($Value.Contains($Name)) { return $Value[$Name] }
    } elseif ($null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]) {
        return $Value.$Name
    }
    return $Default
}

function Test-WorkflowProperty {
    param($Value, [string] $Name)
    if ($Value -is [Collections.IDictionary]) { return $Value.Contains($Name) }
    return $null -ne $Value -and $null -ne $Value.PSObject.Properties[$Name]
}

function Assert-WorkflowPathWithoutLinks {
    param([string] $Path)
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Qualification evidence must not traverse a link or junction: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Initialize-WorkflowEvidenceRoot {
    param([string] $Path, [string] $RunRoot)
    $evidence = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $run = [IO.Path]::GetFullPath($RunRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $comparison = Get-PathComparison
    $separator = [IO.Path]::DirectorySeparatorChar
    if ($evidence.Equals($run, $comparison) -or $evidence.StartsWith($run + $separator, $comparison) -or
        $run.StartsWith($evidence + $separator, $comparison)) {
        throw 'Durable qualification evidence must not overlap the disposable run root.'
    }
    Assert-WorkflowPathWithoutLinks $evidence
    if (Test-Path -LiteralPath $evidence) {
        if (-not (Test-Path -LiteralPath $evidence -PathType Container) -or
            @(Get-ChildItem -LiteralPath $evidence -Force).Count -gt 0) {
            throw 'Durable qualification evidence destination must be absent or an empty directory; existing evidence will not be overwritten.'
        }
    } else { New-Item -ItemType Directory -Path $evidence | Out-Null }
    return $evidence
}

function Invoke-WorkflowCliOperation {
    param([string[]] $Arguments, [string] $WorkingDirectory, [string] $EvidencePath)
    try {
        $processPath = [IO.Path]::ChangeExtension($EvidencePath, '.cli.process.json')
        $result = Invoke-CliJson -Arguments $Arguments -WorkingDirectory $WorkingDirectory -ProcessEvidencePath $processPath
        Write-Utf8Json -Path $EvidencePath -Value $result
        return $result
    } catch {
        Write-Utf8Json -Path $EvidencePath -Value ([ordered]@{succeeded=$false;error=$_.Exception.Message;
            cliFailure=(Get-CorpusCliFailureEvidence $_.Exception);ownedProcess=(Get-CorpusOwnedProcessFailureEvidence $_.Exception)})
        throw
    }
}

function Copy-WorkflowBuildReceipt {
    param([string] $ProjectRoot, [string] $SourcePath, [string] $EvidenceRoot)
    $source = Assert-ContainedPath -Root $ProjectRoot -Path $SourcePath -Label 'Canonical workflow build receipt'
    Assert-WorkflowPathWithoutLinks $source
    $target = Assert-ContainedPath -Root $EvidenceRoot -Path (Join-Path $EvidenceRoot 'build-receipt.json') -Label 'Durable workflow build receipt'
    if (Test-Path -LiteralPath $target) { throw 'Durable build receipt already exists; it will not be overwritten.' }
    Copy-Item -LiteralPath $source -Destination $target
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw 'Copied canonical build receipt identity differs.'
    }
    return $target
}

function Assert-WorkflowNameSet {
    param([object[]] $Expected, [object[]] $Actual, [string] $Label)
    $expectedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $actualSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $Expected) {
        if ([string]::IsNullOrWhiteSpace([string] $name) -or -not $expectedSet.Add([string] $name)) {
            throw "$Label declares an empty or duplicate name."
        }
    }
    foreach ($name in $Actual) {
        if ([string]::IsNullOrWhiteSpace([string] $name) -or -not $actualSet.Add([string] $name)) {
            throw "$Label observed an empty or duplicate name."
        }
    }
    if (-not $expectedSet.SetEquals($actualSet)) {
        throw "$Label differs. Expected=[$($Expected -join ', ')]; Actual=[$($Actual -join ', ')]."
    }
}

function Assert-WorkflowQualificationContract {
    param($Entry)
    $contract = $Entry.qualification
    Assert-WorkflowArtifactName (Get-WorkflowProperty $contract 'artifactName')
    if ($contract.kind -notin @('HybridModuleWorkflow', 'HybridScriptApplication')) {
        throw "Unsupported whole-workflow qualification kind '$($contract.kind)'."
    }
    foreach ($name in @('requiredExports', 'requiredAliases', 'requiredCompiledOwnedFunctions',
        'requiredScenarioIds', 'publicExportScenarios', 'observerScript', 'observerSha256')) {
        if (-not (Test-WorkflowProperty $contract $name)) { throw "Workflow contract requires '$name'." }
    }
    if ([string]::IsNullOrWhiteSpace([string] $Entry.entryPoint) -or
        [string]::IsNullOrWhiteSpace([string] $contract.observerScript) -or
        @($Entry.sourceInputs).Count -eq 0 -or @($contract.requiredScenarioIds).Count -eq 0 -or
        @($contract.requiredCompiledOwnedFunctions).Count -eq 0) {
        throw 'Workflow qualification requires an entrypoint, pinned source inputs, observer, scenarios, and owned functions.'
    }
    if ($contract.observerSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'The reviewed observer requires a pinned SHA-256.' }
    if (Test-LooksLikeRootedPath $contract.observerScript) { throw 'Observer paths must be relative to the trusted packet.' }
    $hasFixture = Test-WorkflowProperty $contract 'fixtureScript'
    if ($hasFixture -ne (Test-WorkflowProperty $contract 'fixtureSha256')) { throw 'Reviewed fixtures require fixtureScript and fixtureSha256 together.' }
    if ($hasFixture) {
        if ([string]::IsNullOrWhiteSpace([string] $contract.fixtureScript) -or
            (Test-LooksLikeRootedPath $contract.fixtureScript) -or $contract.fixtureSha256 -notmatch '^[0-9a-fA-F]{64}$') {
            throw 'Fixture paths must be trusted packet-relative paths with a pinned SHA-256.'
        }
        if ((Test-WorkflowProperty $contract 'testArguments') -or (Test-WorkflowProperty $contract 'testWorkingDirectory')) {
            throw 'A reviewed fixture supplies the test invocation; omit contradictory static testArguments/testWorkingDirectory declarations.'
        }
    } else { Assert-WorkflowTestInvocation $contract }
    Assert-WorkflowNameSet $contract.requiredExports $contract.requiredExports 'Required public exports'
    Assert-WorkflowNameSet $contract.requiredScenarioIds $contract.requiredScenarioIds 'Required scenario IDs'
    $aliases = @($contract.requiredAliases)
    Assert-WorkflowNameSet @($aliases | ForEach-Object { $_.name }) @($aliases | ForEach-Object { $_.name }) 'Required aliases'
    foreach ($alias in $aliases) {
        if ([string]::IsNullOrWhiteSpace([string] $alias.definition)) { throw "Alias '$($alias.name)' has no declared definition." }
    }
    $mapping = $contract.publicExportScenarios
    $mappingNames = if ($mapping -is [Collections.IDictionary]) { @($mapping.Keys) } else { @($mapping.PSObject.Properties | ForEach-Object { $_.Name }) }
    Assert-WorkflowNameSet $contract.requiredExports $mappingNames 'Public-export scenario mapping'
    foreach ($name in $contract.requiredExports) {
        $ids = @(Get-WorkflowProperty $mapping $name)
        if ($ids.Count -eq 0) { throw "Public export '$name' has no declared execution scenario." }
        Assert-WorkflowNameSet $ids $ids "Scenarios for public export '$name'"
        if (@($ids | Where-Object { $_ -notin $contract.requiredScenarioIds }).Count) {
            throw "Public export '$name' maps to an undeclared scenario."
        }
    }
    if ($contract.kind -eq 'HybridModuleWorkflow' -and @($contract.requiredExports).Count -eq 0) {
        throw 'A module workflow must declare its complete public export surface.'
    }
    if ($contract.kind -eq 'HybridScriptApplication' -and
        (@($contract.requiredExports).Count -gt 0 -or $aliases.Count -gt 0)) {
        throw 'An executable script workflow must declare empty module exports and aliases.'
    }
    foreach ($unit in $contract.requiredCompiledOwnedFunctions) {
        if ([string]::IsNullOrWhiteSpace([string] $unit.relativePath) -or [string]::IsNullOrWhiteSpace([string] $unit.name)) {
            throw 'Required compiled-owned functions need relativePath and name, with unitId/startLine when ambiguous.'
        }
    }
    Assert-WorkflowNestedContract $Entry
}

function Assert-WorkflowArtifactName {
    param($Value)
    if ($null -eq $Value) { return }
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace($Value) -or
        $Value.Trim() -in @('.', '..') -or $Value -match '[<>:"/\\|?*\x00-\x1f]' -or
        $Value.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw 'Artifact name must be a portable basename without path separators or invalid filename characters.'
    }
}

function Assert-WorkflowTestInvocation {
    param($Value, [switch] $RequireArguments)
    if ($RequireArguments -and -not (Test-WorkflowProperty $Value 'testArguments')) { throw 'Fixture Start must return testArguments.' }
    if (Test-WorkflowProperty $Value 'testArguments') {
        if ($Value.testArguments -isnot [array]) { throw 'Test arguments must be a literal string array.' }
        foreach ($argument in $Value.testArguments) {
            if ($argument -isnot [string] -or $argument.Contains([char] 0)) { throw 'Test arguments must contain only strings without NUL.' }
        }
    }
    $directory = Get-WorkflowProperty $Value 'testWorkingDirectory'
    if ($null -ne $directory -and ($directory -isnot [string] -or [string]::IsNullOrWhiteSpace($directory) -or
        $directory.Contains([char] 0) -or (Test-LooksLikeRootedPath $directory) -or
        @($directory.Replace('\', '/').Split('/') | Where-Object { $_ -eq '..' }).Count)) {
        throw 'Test working directory must be a contained project-relative directory without NUL or traversal.'
    }
}

function Assert-WorkflowCompiledOwnership {
    param($Contract, $Ledger)
    $checked = [Collections.Generic.List[object]]::new()
    foreach ($required in $Contract.requiredCompiledOwnedFunctions) {
        $unitId = Get-WorkflowProperty $required 'unitId'
        $startLine = Get-WorkflowProperty $required 'startLine'
        $matches = @($Ledger.entries | Where-Object {
            $_.relativePath.Replace('\', '/') -ceq $required.relativePath.Replace('\', '/') -and
            $_.name -ceq $required.name -and (-not $unitId -or $_.unitId -ceq $unitId) -and
            ($null -eq $startLine -or $_.startLine -eq $startLine)
        })
        if ($matches.Count -ne 1) { throw "Required owned function '$($required.relativePath):$($required.name)' is missing or ambiguous." }
        $unit = $matches[0]
        if ($unit.emittedClrMethod -ne $true -or $unit.retainedHostedSource -ne $false -or
            $unit.omitted -ne $false -or $unit.rejected -ne $false -or $unit.shapingFallback -ne $false) {
            throw "Required owned function '$($required.name)' lacks a complete CLR body without retained source or fallback."
        }
        $checked.Add($unit)
    }
    return @($checked)
}

function Assert-WorkflowObservation {
    param($Contract, $Report, [string] $Surface, [string] $ImportPath)
    if ($Report.schemaVersion -ne 1 -or $Report.surface -cne $Surface -or
        -not [IO.Path]::GetFullPath([string] $Report.importPath).Equals([IO.Path]::GetFullPath($ImportPath), (Get-PathComparison))) {
        throw "Observer envelope does not identify the actual '$Surface' surface."
    }
    foreach ($name in @('actualExports', 'actualAliases', 'scenarios')) {
        if (-not (Test-WorkflowProperty $Report $name)) { throw "Observer report requires '$name'." }
    }
    Assert-WorkflowNameSet $Contract.requiredExports $Report.actualExports "$Surface public exports"
    $aliases = @($Report.actualAliases)
    Assert-WorkflowNameSet @($Contract.requiredAliases | ForEach-Object { $_.name }) @($aliases | ForEach-Object { $_.name }) "$Surface aliases"
    foreach ($expected in $Contract.requiredAliases) {
        $actual = @($aliases | Where-Object name -EQ $expected.name)[0]
        if ($actual.definition -cne $expected.definition) { throw "$Surface alias '$($expected.name)' targets the wrong command." }
    }
    Assert-WorkflowNameSet $Contract.requiredScenarioIds @($Report.scenarios | ForEach-Object { $_.scenarioId }) "$Surface scenario inventory"
    foreach ($scenario in $Report.scenarios) {
        if ($scenario.succeeded -isnot [bool] -or -not $scenario.succeeded -or
            $null -eq (Get-WorkflowProperty $scenario 'observation') -or
            -not (Test-WorkflowProperty $scenario 'executedExports')) {
            throw "$Surface scenario '$($scenario.scenarioId)' failed or lacks an execution observation."
        }
        Assert-WorkflowNameSet $scenario.executedExports $scenario.executedExports "$Surface scenario executed exports"
        if (@($scenario.executedExports | Where-Object { $_ -notin $Contract.requiredExports }).Count) {
            throw "$Surface scenario '$($scenario.scenarioId)' reports an undeclared public export."
        }
    }
    foreach ($name in $Contract.requiredExports) {
        foreach ($id in @(Get-WorkflowProperty $Contract.publicExportScenarios $name)) {
            $scenario = @($Report.scenarios | Where-Object scenarioId -EQ $id)[0]
            if ($name -notin $scenario.executedExports) {
                throw "$Surface scenario '$id' did not report executing required public export '$name'."
            }
        }
    }
}

function Assert-WorkflowObservationParity {
    param($Contract, $Original, $Actual)
    foreach ($id in $Contract.requiredScenarioIds) {
        $reference = @($Original.scenarios | Where-Object scenarioId -EQ $id)[0]
        $observed = @($Actual.scenarios | Where-Object scenarioId -EQ $id)[0]
        $left = ConvertTo-Json -InputObject $reference.observation -Depth 100 -Compress
        $right = ConvertTo-Json -InputObject $observed.observation -Depth 100 -Compress
        if ($left -cne $right) { throw "Scenario '$id' observation differs between Original and $($Actual.surface)." }
    }
}

function Copy-WorkflowSourceInputs {
    param($Entry, [string] $SourceRoot, [string] $Destination)
    if ([string]::IsNullOrWhiteSpace($SourceRoot)) { throw 'Whole-workflow qualification requires -SourceRoot with reviewed local inputs.' }
    $root = [IO.Path]::GetFullPath($SourceRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "SourceRoot does not exist: $root" }
    if ((Get-Item -LiteralPath $root).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SourceRoot must not be a link or junction.' }
    $paths = @($Entry.sourceInputs | ForEach-Object { $_.path })
    Assert-WorkflowNameSet $paths $paths 'Pinned source paths'
    if ($Entry.entryPoint -notin $paths) { throw 'The entrypoint must be included in pinned sourceInputs.' }
    foreach ($required in @(Get-WorkflowProperty $Entry.qualification 'requiredSourceFiles' @())) {
        if ($required -notin $paths) { throw "Required source file '$required' is not pinned." }
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($input in $Entry.sourceInputs) {
        if ((Test-LooksLikeRootedPath $input.path) -or
            @($input.path.Replace('\', '/').Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) {
            throw 'Pinned source paths must be portable relative paths without empty or traversal segments.'
        }
        if ($input.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Invalid source SHA-256 for '$($input.path)'." }
        $source = Assert-ContainedPath -Root $root -Path (Join-Path $root $input.path) -Label 'Pinned workflow source'
        $ancestor = Get-Item -LiteralPath $source -Force
        while ($ancestor.FullName -ne $root) {
            if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Pinned workflow source traverses a link: $source" }
            $ancestor = if ($ancestor -is [IO.DirectoryInfo]) { $ancestor.Parent } else { $ancestor.Directory }
            if ($null -eq $ancestor) { throw 'Pinned source ancestor could not be verified.' }
        }
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or
            (Get-FileHash -LiteralPath $source).Hash -ne $input.sha256) { throw "Pinned source identity mismatch: $($input.path)" }
        $target = Assert-ContainedPath -Root $Destination -Path (Join-Path $Destination $input.path) -Label 'Staged workflow source'
        New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $target
        if ((Get-FileHash -LiteralPath $target).Hash -ne $input.sha256) { throw "Staged source identity mismatch: $($input.path)" }
    }
}

function Invoke-WorkflowObserver {
    param($Entry, [string] $ObserverPath, [string] $ImportPath, [string] $Surface,
        [string] $ScenarioRoot, [string] $EvidenceRoot, [string] $HostExecutable)
    $reportPath = Join-Path $EvidenceRoot ($Surface + '.json')
    # Set built-in module search paths inside the selected host, so desktop qualification
    # never inherits the caller's PowerShell 7 module directory or original source root.
    $command = '$env:PSModulePath = Join-Path $PSHOME ''Modules''; $ErrorActionPreference = ''Stop''; & ' +
        (Quote-PowerShellLiteral $ObserverPath) + ' -ImportPath ' + (Quote-PowerShellLiteral $ImportPath) +
        ' -Surface ' + (Quote-PowerShellLiteral $Surface) + ' -ScenarioRoot ' + (Quote-PowerShellLiteral $ScenarioRoot) +
        ' -ResultPath ' + (Quote-PowerShellLiteral $reportPath)
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $run = Invoke-OwnedProcess -FileName $HostExecutable -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive',
        '-EncodedCommand', $encoded) -WorkingDirectory $EvidenceRoot -Environment @{ POWERSHELL_TELEMETRY_OPTOUT = '1' }
    Write-Utf8Json -Path (Join-Path $EvidenceRoot ($Surface + '.process.json')) -Value $run
    if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "$Surface observer failed with exit $($run.ExitCode); see retained process evidence."
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    Assert-WorkflowObservation $Entry.qualification $report $Surface $ImportPath
    return $report
}

function Assert-WorkflowSourceIdentities {
    param($Entry, [string] $SourceRoot)
    foreach ($input in $Entry.sourceInputs) {
        $path = Assert-ContainedPath -Root $SourceRoot -Path (Join-Path $SourceRoot $input.path) -Label 'Qualified source input'
        if ((Get-FileHash -LiteralPath $path).Hash -ne $input.sha256) { throw "Qualified source input changed: $($input.path)" }
    }
}

function Invoke-WorkflowFixture {
    param($Contract, [string] $FixturePath, [ValidateSet('Start', 'Stop')] [string] $Operation,
        [string] $ScenarioRoot, [string] $EvidenceRoot)
    if ((Get-FileHash -LiteralPath $FixturePath).Hash -ne $Contract.fixtureSha256) { throw 'Reviewed fixture script identity changed.' }
    $resultPath = Join-Path $EvidenceRoot ('fixture.' + $Operation + '.json')
    $run = Invoke-OwnedProcess -FileName 'pwsh' -Arguments @('-NoLogo', '-NoProfile', '-NonInteractive', '-File',
        $FixturePath, '-Operation', $Operation, '-ScenarioRoot', $ScenarioRoot, '-ResultPath', $resultPath) `
        -WorkingDirectory $EvidenceRoot -TimeoutSeconds 120 `
        -Environment @{ PSModulePath = (Join-Path $PSHOME 'Modules'); POWERSHELL_TELEMETRY_OPTOUT = '1' }
    Write-Utf8Json -Path (Join-Path $EvidenceRoot ('fixture.' + $Operation + '.process.json')) -Value $run
    if ($run.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw "Reviewed fixture $Operation failed; retained process evidence identifies its owned state." }
    $receipt = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($receipt.schemaVersion -ne 1 -or $receipt.operation -cne $Operation) { throw 'Reviewed fixture returned an invalid operation envelope.' }
    if ($Operation -eq 'Start') {
        Assert-WorkflowTestInvocation $receipt -RequireArguments
        if ($null -eq (Get-WorkflowProperty $receipt 'fixtureIdentity')) { throw 'Fixture Start must return its durable owned identity.' }
    } elseif ($receipt.stopped -isnot [bool] -or -not $receipt.stopped) { throw 'Reviewed fixture Stop did not confirm cleanup.' }
    return $receipt
}

function Invoke-WorkflowArtifactObserver {
    param($Entry, [string] $ObserverPath, [string] $ImportPath, [string] $Surface,
        [string] $ScenarioRoot, [string] $EvidenceRoot, [string] $HostExecutable,
        [string] $ProjectRoot, [string] $InputRoot)
    $source = Assert-ContainedPath -Root $ProjectRoot -Path $InputRoot -Label 'Owned staged source withdrawal'
    $holding = Assert-ContainedPath -Root $ProjectRoot -Path ($InputRoot + '.withdrawn') -Label 'Owned staged source holding path'
    if (Test-Path -LiteralPath $holding) { throw 'Staged-source holding path already exists; preserve it for recovery.' }
    if ((Get-Item -LiteralPath $source).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Staged source must not be a link.' }
    # This is the fresh, copied input tree, not the caller's source or a checkout.
    # Build has exited. Restore before invoking any receipt-validating project operation.
    Move-Item -LiteralPath $source -Destination $holding
    try {
        if (Test-Path -LiteralPath $source) { throw 'The compiler input path remains available during artifact observation.' }
        Write-Utf8Json -Path (Join-Path $EvidenceRoot ($Surface + '.source-absence.json')) -Value ([ordered]@{
            compilerInputPath = $source; ownedHoldingPath = $holding; compilerInputPathUnavailable = $true
            scope = 'The compiler input tree was withdrawn during this fresh artifact observer; selected-host built-in module paths only.'
        })
        $report = Invoke-WorkflowObserver $Entry $ObserverPath $ImportPath $Surface $ScenarioRoot $EvidenceRoot $HostExecutable
        return $report
    } finally {
        if (Test-Path -LiteralPath $source) { throw "Artifact observation recreated the compiler input path. Preserve both source trees for recovery: $holding" }
        Move-Item -LiteralPath $holding -Destination $source
        Assert-WorkflowSourceIdentities $Entry $source
    }
}

function Invoke-WholeWorkflowQualification {
    param($Entry, $Packet, [string] $PacketPath, [string] $SourceRoot, [string] $RunRoot, [switch] $Offline,
        [string] $EvidenceRoot)
    Assert-WorkflowQualificationContract $Entry
    if ($Entry.id -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') { throw 'Workflow id must be a portable filename component.' }
    $root = Assert-ContainedPath -Root $RunRoot -Path (Join-Path $RunRoot $Entry.id) -Label 'Workflow project'
    $inputRoot = Join-Path $root 'source'
    $durableEvidence = -not [string]::IsNullOrWhiteSpace($EvidenceRoot)
    if ($durableEvidence) { $EvidenceRoot = Initialize-WorkflowEvidenceRoot $EvidenceRoot $RunRoot }
    else {
        $EvidenceRoot = Join-Path $root 'evidence'
        New-Item -ItemType Directory -Path $EvidenceRoot -Force | Out-Null
    }
    Copy-WorkflowSourceInputs $Entry $SourceRoot $inputRoot
    $sourcePath = Assert-ContainedPath -Root $inputRoot -Path (Join-Path $inputRoot $Entry.entryPoint) -Label 'Workflow entrypoint'
    $observer = Assert-ContainedPath -Root (Split-Path $PacketPath) -Path (Join-Path (Split-Path $PacketPath) $Entry.qualification.observerScript) -Label 'Trusted workflow observer'
    if (-not (Test-Path -LiteralPath $observer -PathType Leaf)) { throw "Trusted observer does not exist: $observer" }
    $observerHash = (Get-FileHash -LiteralPath $observer).Hash
    if ($observerHash -ne $Entry.qualification.observerSha256) { throw 'Trusted observer differs from its reviewed SHA-256.' }
    $observerInputs = @([pscustomobject]@{ Path = $observer; Sha256 = $observerHash })
    foreach ($support in (@(Get-WorkflowProperty $Entry.qualification 'observerSupport' @()) + @(Get-WorkflowProperty $Entry.qualification 'fixtureSupport' @()))) {
        if (Test-LooksLikeRootedPath $support.path) { throw 'Observer support paths must be relative to the trusted packet.' }
        $path = Assert-ContainedPath -Root (Split-Path $PacketPath) -Path (Join-Path (Split-Path $PacketPath) $support.path) -Label 'Trusted observer support'
        if ($support.sha256 -notmatch '^[0-9a-fA-F]{64}$' -or (Get-FileHash -LiteralPath $path).Hash -ne $support.sha256) {
            throw "Trusted observer support identity mismatch: $($support.path)"
        }
        $observerInputs += [pscustomobject]@{ Path = $path; Sha256 = $support.sha256 }
    }
    $scenarioRoot = Join-Path $root 'scenarios'
    New-Item -ItemType Directory -Path $scenarioRoot -Force | Out-Null
    $fixturePath = $null
    if (Test-WorkflowProperty $Entry.qualification 'fixtureScript') {
        $fixturePath = Assert-ContainedPath -Root (Split-Path $PacketPath) -Path (Join-Path (Split-Path $PacketPath) $Entry.qualification.fixtureScript) -Label 'Reviewed fixture script'
    }
    $fixtureAttempted = $false
    $fixtureStart = $null
    $qualificationResult = $null
    try {
    $testInvocation = $Entry.qualification
    if ($fixturePath) {
        $fixtureAttempted = $true
        $fixtureStart = Invoke-WorkflowFixture $Entry.qualification $fixturePath 'Start' $scenarioRoot $evidenceRoot
        $testInvocation = $fixtureStart
    }
    $hostExecutable = if ($Packet.targetFramework -eq 'net472') {
        if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
            throw 'net472 workflow observation requires Windows PowerShell on Windows.'
        }
        'powershell.exe'
    } else { 'pwsh' }
    $original = Invoke-WorkflowObserver $Entry $observer $sourcePath 'Original' $scenarioRoot $evidenceRoot $hostExecutable
    Assert-WorkflowSourceIdentities $Entry $inputRoot
    $projectPath = Join-Path $root 'powerforge.psproject.json'
    $kind = if ($Entry.qualification.kind -eq 'HybridModuleWorkflow') { 'dll' } else { 'exe' }
    $initArgs = @('powershell', 'project', 'init', $sourcePath, '--project', $projectPath, '--name',
        ('Qualification-' + $Entry.id), '--kind', $kind, '--mode', 'Hybrid', '--framework', $Packet.targetFramework,
        '--semantic-profile', $Packet.semanticProfile, '--output', 'json')
    $rid = Get-WorkflowProperty $Entry.qualification 'runtimeIdentifier'
    if ($rid) { $initArgs += @('--rid', $rid) }
    $initial = Invoke-WorkflowCliOperation -Arguments $initArgs -WorkingDirectory $root -EvidencePath (Join-Path $evidenceRoot 'init.json')
    $project = Get-Content -LiteralPath $projectPath -Raw | ConvertFrom-Json
    $project.resources.mode = if ($kind -eq 'dll') { 'CompleteModule' } else { 'Declared' }
    foreach ($artifact in $project.artifacts) {
        $artifactName = Get-WorkflowProperty $Entry.qualification 'artifactName'
        if ($null -ne $artifactName) {
            $artifact | Add-Member -NotePropertyName artifactName -NotePropertyValue $artifactName.Trim() -Force
        }
        $artifact | Add-Member -NotePropertyName testArguments -NotePropertyValue @(Get-WorkflowProperty $testInvocation 'testArguments' @()) -Force
        $artifact | Add-Member -NotePropertyName testWorkingDirectory -NotePropertyValue (Get-WorkflowProperty $testInvocation 'testWorkingDirectory') -Force
    }
    Write-Utf8Json -Path $projectPath -Value $project
    $steps = [ordered]@{}
    foreach ($operation in @('analyze', 'lock', 'restore', 'build')) {
        $arguments = @('powershell', 'project', $operation, $projectPath, '--output', 'json')
        if ($operation -eq 'restore' -and $Offline) { $arguments += '--offline' }
        $steps[$operation] = Invoke-WorkflowCliOperation -Arguments $arguments -WorkingDirectory $root -EvidencePath (Join-Path $evidenceRoot ($operation + '.json'))
    }
    $built = @($steps.build.targets)
    if ($built.Count -ne 1) { throw 'Workflow qualification requires exactly one declared project target.' }
    $target = @($project.artifacts)[0]
    $canonicalBuildReceiptPath = Join-Path $root ('.powerforge/build/' + $target.name + '.json')
    $buildReceiptPath = Copy-WorkflowBuildReceipt $root $canonicalBuildReceiptPath $evidenceRoot
    $buildReceipt = Get-Content -LiteralPath $buildReceiptPath -Raw | ConvertFrom-Json
    $owned = @(Assert-WorkflowCompiledOwnership $Entry.qualification $buildReceipt.manifest.unitDispositionLedger)
    $outputRoot = Assert-ContainedPath -Root $root -Path (Join-Path $root $target.outputDirectory) -Label 'Built workflow output'
    $nested = @(Assert-WorkflowNestedEmission $Entry $buildReceipt.manifest $outputRoot)
    Write-Utf8Json -Path (Join-Path $evidenceRoot 'nested-emission.json') -Value ([ordered]@{requiredCount=$nested.Count;compiledNestedFunctions=$nested})
    $builtPath = Assert-ContainedPath -Root $outputRoot -Path $built[0].path -Label 'Built workflow artifact'
    $builtReport = Invoke-WorkflowArtifactObserver $Entry $observer $builtPath 'Built' $scenarioRoot $evidenceRoot $hostExecutable $root $inputRoot
    Assert-WorkflowObservationParity $Entry.qualification $original $builtReport
    foreach ($operation in @('test', 'pack', 'install')) {
        $steps[$operation] = Invoke-WorkflowCliOperation -Arguments @('powershell', 'project', $operation, $projectPath, '--output', 'json') -WorkingDirectory $root -EvidencePath (Join-Path $evidenceRoot ($operation + '.json'))
    }
    $installRoot = [string] @($steps.install.targets)[0].path
    $relativePrimary = [IO.Path]::GetRelativePath($outputRoot, $builtPath)
    $installedPath = Assert-ContainedPath -Root $installRoot -Path (Join-Path $installRoot ('artifact/' + $relativePrimary)) -Label 'Installed workflow primary'
    if ((Get-FileHash -LiteralPath $builtPath).Hash -ne $buildReceipt.manifest.artifactSha256 -or
        (Get-FileHash -LiteralPath $installedPath).Hash -ne $buildReceipt.manifest.artifactSha256) {
        throw 'Built/installed primary bytes differ from the canonical authenticated artifact identity.'
    }
    foreach ($identity in $observerInputs) {
        if ((Get-FileHash -LiteralPath $identity.Path).Hash -ne $identity.Sha256) { throw 'Trusted observer input changed during qualification.' }
    }
    $installedReport = Invoke-WorkflowArtifactObserver $Entry $observer $installedPath 'Installed' $scenarioRoot $evidenceRoot $hostExecutable $root $inputRoot
    Assert-WorkflowObservationParity $Entry.qualification $original $installedReport
    $qualificationResult = [ordered]@{
        id = $Entry.id; kind = $Entry.qualification.kind; succeeded = $true; emittedClrMethod = $null
        completeWorkloadExecution = $false; wholeArtifactWorkflowQualified = $true
        scenarioCount = @($Entry.qualification.requiredScenarioIds).Count; scenarioSurfaces = @('Original', 'Built', 'Installed')
        requiredExports = @($Entry.qualification.requiredExports); requiredAliases = @($Entry.qualification.requiredAliases)
        compiledOwnedFunctions = $owned; unitDispositionLedger = $buildReceipt.manifest.unitDispositionLedger
        compiledNestedFunctions = $nested
        originalPath = $sourcePath; builtPath = $builtPath; installedPath = $installedPath
        observerSha256 = $observerHash; projectSha256 = (Get-FileHash -LiteralPath $projectPath).Hash
        observerHost = $hostExecutable
        buildReceiptPath = $buildReceiptPath; evidenceRoot = $evidenceRoot; lifecycle = $steps
        artifactPathsAreHistorical = $durableEvidence
        scenarioParity = $true; noSourceModulePathFallback = $true
        compilerInputUnavailableDuringArtifactObservation = $true
        interpretation = 'Whole artifact surface and declared scenarios passed through reviewed observer reports and the canonical project lifecycle. Emission and absence of retained source are required for named owned functions; native binding and hosted command regions remain reported. This is not exhaustive branch coverage or PowerShell-free execution.'
    }
    } catch {
        Write-Utf8Json -Path (Join-Path $evidenceRoot 'qualification-failure.json') -Value ([ordered]@{
            id=$Entry.id;succeeded=$false;error=$_.Exception.Message;evidenceRoot=$evidenceRoot;ownedProcess=(Get-CorpusOwnedProcessFailureEvidence $_.Exception)
        })
        throw
    } finally {
        if ($fixtureAttempted) {
            try {
                $fixtureStop = Invoke-WorkflowFixture $Entry.qualification $fixturePath 'Stop' $scenarioRoot $evidenceRoot
                if ($fixtureStart -and (ConvertTo-Json -InputObject $fixtureStart.fixtureIdentity -Depth 30 -Compress) -cne
                    (ConvertTo-Json -InputObject $fixtureStop.fixtureIdentity -Depth 30 -Compress)) { throw 'Fixture Stop identified a different owned fixture.' }
                if ($qualificationResult) {
                    $qualificationResult['fixtureStart'] = $fixtureStart
                    $qualificationResult['fixtureStop'] = $fixtureStop
                }
            } catch {
                Write-Utf8Json -Path (Join-Path $evidenceRoot 'fixture-cleanup-failure.json') -Value ([ordered]@{succeeded=$false;error=$_.Exception.Message;ownedProcess=(Get-CorpusOwnedProcessFailureEvidence $_.Exception)})
                throw
            }
        }
    }
    Write-Utf8Json -Path (Join-Path $evidenceRoot 'qualification-result.json') -Value $qualificationResult
    return $qualificationResult
}
