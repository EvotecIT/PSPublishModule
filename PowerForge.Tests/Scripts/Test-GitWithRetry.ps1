[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$actionScript = Join-Path $repositoryRoot '.github/actions/powerforge-server-backup/Invoke-PowerForgeServerBackup.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($actionScript, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Unable to parse the server backup action: $($parseErrors[0].Message)"
}

$functionAst = $ast.Find({
        param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Invoke-GitWithRetry'
    }, $true)
if ($null -eq $functionAst) {
    throw 'Invoke-GitWithRetry was not found in the server backup action.'
}
. ([scriptblock]::Create($functionAst.Extent.Text))

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "powerforge-git-retry-$([Guid]::NewGuid().ToString('N'))"
$resetPath = Join-Path $testRoot 'partial-clone'
$script:attempt = 0
$script:exitCodes = @()
$script:expectedArguments = @('clone', '--depth', '1', 'repository', $resetPath)

function git {
    param([Parameter(ValueFromRemainingArguments)][object[]] $GitArguments)

    $script:attempt++
    if (Test-Path -LiteralPath $resetPath) {
        throw "Partial clone was not reset before attempt $script:attempt."
    }
    if ([string]::Join("`n", $GitArguments) -ne [string]::Join("`n", $script:expectedArguments)) {
        throw 'Git arguments changed while retrying.'
    }

    $exitCode = $script:exitCodes[$script:attempt - 1]
    New-Item -ItemType Directory -Path $resetPath | Out-Null
    $global:LASTEXITCODE = $exitCode
}

try {
    New-Item -ItemType Directory -Path $resetPath -Force | Out-Null
    $script:attempt = 0
    $script:exitCodes = @(128, 128, 0)
    Invoke-GitWithRetry -Operation 'Transient clone' -Arguments $script:expectedArguments -ResetPath $resetPath -MaxAttempts 3 -RetryDelaySeconds 0
    if ($script:attempt -ne 3 -or -not (Test-Path -LiteralPath $resetPath)) {
        throw 'Transient clone retry did not preserve the successful checkout from the third attempt.'
    }

    $script:attempt = 0
    $script:exitCodes = @(128, 128, 128)
    $terminalError = $null
    try {
        Invoke-GitWithRetry -Operation 'Terminal clone' -Arguments $script:expectedArguments -ResetPath $resetPath -MaxAttempts 3 -RetryDelaySeconds 0
    } catch {
        $terminalError = $_
    }
    if ($null -eq $terminalError -or $terminalError.Exception.Message -notmatch 'failed after 3 attempts') {
        throw 'Terminal clone failure was not surfaced after the configured retry limit.'
    }
    if ($script:attempt -ne 3 -or (Test-Path -LiteralPath $resetPath)) {
        throw 'Terminal clone retry did not clean the final partial checkout.'
    }

    Remove-Item Function:\git
    $remote = Join-Path $testRoot 'remote.git'
    $seed = Join-Path $testRoot 'seed'
    $workerA = Join-Path $testRoot 'worker-a'
    $workerB = Join-Path $testRoot 'worker-b'
    $workerC = Join-Path $testRoot 'worker-c'

    function Invoke-TestGit {
        param([Parameter(ValueFromRemainingArguments)][object[]] $GitArguments)

        & git @GitArguments | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Test Git command failed with exit code ${LASTEXITCODE}: git $GitArguments"
        }
    }

    Invoke-TestGit init --bare $remote
    Invoke-TestGit init $seed
    Invoke-TestGit -C $seed checkout -b main
    Invoke-TestGit -C $seed config user.name PowerForgeTest
    Invoke-TestGit -C $seed config user.email powerforge-test@example.invalid
    Set-Content -LiteralPath (Join-Path $seed 'base.txt') -Value base -Encoding utf8NoBOM
    Invoke-TestGit -C $seed add base.txt
    Invoke-TestGit -C $seed commit -m base
    Invoke-TestGit -C $seed remote add origin $remote
    Invoke-TestGit -C $seed push -u origin main

    $remoteUri = ([Uri]$remote).AbsoluteUri
    $cloneArguments = @('clone', '--depth', '1', '--no-tags', '--single-branch', '--branch', 'main', $remoteUri, $workerA)
    Invoke-GitWithRetry -Operation 'Shallow worker A clone' -Arguments $cloneArguments -ResetPath $workerA -RetryDelaySeconds 0
    Invoke-TestGit clone --depth 1 --no-tags --single-branch --branch main $remoteUri $workerB
    foreach ($worker in @($workerA, $workerB)) {
        Invoke-TestGit -C $worker config user.name PowerForgeTest
        Invoke-TestGit -C $worker config user.email powerforge-test@example.invalid
    }

    Set-Content -LiteralPath (Join-Path $workerA 'worker-a.txt') -Value worker-a -Encoding utf8NoBOM
    Invoke-TestGit -C $workerA add worker-a.txt
    Invoke-TestGit -C $workerA commit -m worker-a

    Set-Content -LiteralPath (Join-Path $workerB 'worker-b.txt') -Value worker-b -Encoding utf8NoBOM
    Invoke-TestGit -C $workerB add worker-b.txt
    Invoke-TestGit -C $workerB commit -m worker-b
    Invoke-TestGit -C $workerB push origin HEAD:main
    $workerBHead = (& git -C $workerB rev-parse HEAD).Trim()
    Invoke-TestGit -C $workerB rev-parse HEAD

    $fetchArguments = @('-C', $workerA, 'fetch', '--no-tags', 'origin', '+refs/heads/main:refs/remotes/origin/main')
    Invoke-GitWithRetry -Operation 'Advance shallow origin/main' -Arguments $fetchArguments -RetryDelaySeconds 0
    $workerAOrigin = (& git -C $workerA rev-parse origin/main).Trim()
    if ($LASTEXITCODE -ne 0 -or $workerAOrigin -ne $workerBHead) {
        throw 'Exact shallow fetch did not advance origin/main to the competing commit.'
    }
    Invoke-TestGit -C $workerA rebase origin/main

    Invoke-TestGit clone --depth 1 --no-tags --single-branch --branch main $remoteUri $workerC
    Invoke-TestGit -C $workerC config user.name PowerForgeTest
    Invoke-TestGit -C $workerC config user.email powerforge-test@example.invalid
    Set-Content -LiteralPath (Join-Path $workerC 'worker-c.txt') -Value worker-c -Encoding utf8NoBOM
    Invoke-TestGit -C $workerC add worker-c.txt
    Invoke-TestGit -C $workerC commit -m worker-c
    Invoke-TestGit -C $workerC push origin HEAD:main

    & git -C $workerA push origin HEAD:main 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw 'The first worker A push unexpectedly ignored the competing publication.'
    }

    Invoke-GitWithRetry -Operation 'Refresh after rejected push' -Arguments $fetchArguments -RetryDelaySeconds 0
    Invoke-TestGit -C $workerA rebase origin/main
    Invoke-TestGit -C $workerA push origin HEAD:main
    $remoteHead = (& git --git-dir=$remote rev-parse main).Trim()
    $workerAHead = (& git -C $workerA rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $remoteHead -ne $workerAHead) {
        throw 'Fetch, rebase, and retry did not publish the local backup commit.'
    }
    foreach ($requiredPath in @('worker-a.txt', 'worker-b.txt', 'worker-c.txt')) {
        & git --git-dir=$remote cat-file -e "main:$requiredPath"
        if ($LASTEXITCODE -ne 0) {
            throw "Published history does not retain $requiredPath."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
