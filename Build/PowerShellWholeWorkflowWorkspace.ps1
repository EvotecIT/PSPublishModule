# CI workspace ownership shared by creation and terminal cleanup.
. (Join-Path $PSScriptRoot '../Benchmarks/PowerShellCompilation/Corpus/Corpus.Runner.Common.ps1')
. (Join-Path $PSScriptRoot '../Benchmarks/PowerShellCompilation/Corpus/Corpus.Qualification.Workflow.ps1')

function Get-WholeWorkflowWorkspaceBases {
    param([string] $RunnerTemp)
    if (-not [IO.Path]::IsPathFullyQualified($RunnerTemp)) { throw 'Runner temp must be an absolute existing directory.' }
    $current = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RunnerTemp))
    if (-not (Test-Path -LiteralPath $current -PathType Container)) { throw 'Runner temp and its volume must already exist.' }
    Assert-WorkflowPathWithoutLinks $current
    while ($current) {
        # A ten-character leaf plus its separator must fit the gate's 25-character budget.
        if ((Join-Path $current 'pf00000000').Length -le 25) { $current }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}

function Write-NewWholeWorkflowWorkspaceRecord {
    param([string] $Path, [string] $Text)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::Read)
    $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
    try { $writer.Write($Text) } finally { $writer.Dispose() }
}

function New-WholeWorkflowWorkspace {
    param([Parameter(Mandatory)] [string] $RunnerTemp)
    $bases = @(Get-WholeWorkflowWorkspaceBases $RunnerTemp)
    $RunnerTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RunnerTemp))
    $gateRoot = Join-Path $RunnerTemp 'powerforge-compiler-gate'
    Assert-WorkflowPathWithoutLinks $gateRoot
    $recordPath = Join-Path $gateRoot 'whole-workflow-workspace.json'
    if ((Test-Path -LiteralPath $recordPath) -or (Test-Path -LiteralPath (Join-Path $gateRoot 'whole-workflow-root.txt'))) {
        throw 'Prior whole-workflow ownership must be preserved; choose a fresh job workspace.'
    }
    if (-not (Test-Path -LiteralPath $gateRoot -PathType Container)) { New-Item -ItemType Directory -Path $gateRoot | Out-Null }
    foreach ($base in $bases) {
        $ownershipId = [guid]::NewGuid().ToString('N')
        $workspace = Join-Path $base ('pf' + $ownershipId.Substring(0, 8))
        # No Force: a collision or denied ancestor is never adopted as our workspace.
        try { New-Item -ItemType Directory -Path $workspace -ErrorAction Stop | Out-Null }
        catch [UnauthorizedAccessException] { continue }
        catch [IO.IOException] { continue }
        Assert-WorkflowPathWithoutLinks $workspace
        $owner = [ordered]@{ schemaVersion = 1; kind = 'PowerForge.WholeWorkflowWorkspace'; ownershipId = $ownershipId
            runnerTemp = $RunnerTemp; shortBase = $base; workspacePath = $workspace; createdUtc = [DateTime]::UtcNow.ToString('o') }
        $text = ($owner | ConvertTo-Json -Compress) + [Environment]::NewLine
        Write-NewWholeWorkflowWorkspaceRecord -Path (Join-Path $workspace '.powerforge-whole-workflow-owner.json') -Text $text
        Write-NewWholeWorkflowWorkspaceRecord -Path $recordPath -Text $text
        return [pscustomobject]$owner
    }
    throw 'No writable short workspace ancestor exists on the runner temp volume; grant the runner a short writable directory before qualification.'
}

function Get-OwnedWholeWorkflowWorkspace {
    param([Parameter(Mandatory)] [string] $RunnerTemp)
    $bases = @(Get-WholeWorkflowWorkspaceBases $RunnerTemp)
    $RunnerTemp = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($RunnerTemp))
    $gateRoot = Join-Path $RunnerTemp 'powerforge-compiler-gate'
    $recordPath = Join-Path $gateRoot 'whole-workflow-workspace.json'
    Assert-WorkflowPathWithoutLinks $recordPath
    if (-not (Test-Path -LiteralPath $recordPath -PathType Leaf)) {
        if ((Test-Path -LiteralPath (Join-Path $gateRoot 'whole-workflow-root.txt')) -or
            (Test-Path -LiteralPath (Join-Path $gateRoot 'evidence/whole-workflows'))) {
            throw 'Whole-workflow state exists without its exact workspace ownership record.'
        }
        return $null
    }
    $text = Get-Content -LiteralPath $recordPath -Raw
    $owner = $text | ConvertFrom-Json
    # ConvertFrom-Json may infer DateTime and trim fractional zeros on serialization.
    # Keep the creation identity exactly as recorded without a newer PowerShell requirement.
    $document = [Text.Json.JsonDocument]::Parse($text)
    try { $owner.createdUtc = $document.RootElement.GetProperty('createdUtc').GetString() }
    finally { $document.Dispose() }
    $comparison = Get-PathComparison
    if ($owner.schemaVersion -ne 1 -or $owner.kind -cne 'PowerForge.WholeWorkflowWorkspace' -or
        $owner.ownershipId -cnotmatch '^[0-9a-f]{32}$' -or -not $RunnerTemp.Equals($owner.runnerTemp, $comparison) -or
        -not @($bases | Where-Object { $_.Equals($owner.shortBase, $comparison) }).Count) {
        throw 'Whole-workflow ownership does not match this runner temp and an admitted short ancestor.'
    }
    $expected = Join-Path $owner.shortBase ('pf' + $owner.ownershipId.Substring(0, 8))
    if (-not $expected.Equals($owner.workspacePath, $comparison) -or $expected.Length -gt 25) {
        throw 'Whole-workflow ownership does not identify its exact short workspace.'
    }
    Assert-WorkflowPathWithoutLinks $expected
    if (Test-Path -LiteralPath $expected) {
        $marker = Join-Path $expected '.powerforge-whole-workflow-owner.json'
        Assert-WorkflowPathWithoutLinks $marker
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw) -cne $text) {
            throw 'Whole-workflow workspace marker does not match its recorded creation identity.'
        }
    }
    return $owner
}
