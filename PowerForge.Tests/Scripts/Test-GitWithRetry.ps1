[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$supportScript = Join-Path $repositoryRoot '.github/actions/powerforge-server-backup/PowerForgeBackupSupport.ps1'
. $supportScript

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

    $catalogRemote = Join-Path $testRoot 'catalog-remote.git'
    $catalogSeed = Join-Path $testRoot 'catalog-seed'
    $catalogWorkerA = Join-Path $testRoot 'catalog-worker-a'
    $catalogWorkerB = Join-Path $testRoot 'catalog-worker-b'
    Invoke-TestGit init --bare $catalogRemote
    Invoke-TestGit init $catalogSeed
    Invoke-TestGit -C $catalogSeed checkout -b main
    Invoke-TestGit -C $catalogSeed config user.name PowerForgeTest
    Invoke-TestGit -C $catalogSeed config user.email powerforge-test@example.invalid
    New-Item -ItemType Directory -Path (Join-Path $catalogSeed 'server') | Out-Null
    Set-Content -LiteralPath (Join-Path $catalogSeed 'server/LATEST.txt') -Value seed -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $catalogSeed 'server/index.json') -Value '{"latest":"seed"}' -Encoding utf8NoBOM
    Invoke-TestGit -C $catalogSeed add server
    Invoke-TestGit -C $catalogSeed commit -m seed
    Invoke-TestGit -C $catalogSeed remote add origin $catalogRemote
    Invoke-TestGit -C $catalogSeed push -u origin main

    $catalogRemoteUri = ([Uri]$catalogRemote).AbsoluteUri
    foreach ($catalogWorker in @($catalogWorkerA, $catalogWorkerB)) {
        Invoke-TestGit clone --no-tags --single-branch --branch main $catalogRemoteUri $catalogWorker
        Invoke-TestGit -C $catalogWorker config user.name PowerForgeTest
        Invoke-TestGit -C $catalogWorker config user.email powerforge-test@example.invalid
    }
    New-Item -ItemType Directory -Path (Join-Path $catalogWorkerA 'server/capture-a') | Out-Null
    Set-Content -LiteralPath (Join-Path $catalogWorkerA 'server/capture-a/data.txt') -Value capture-a -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $catalogWorkerA 'server/LATEST.txt') -Value capture-a -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $catalogWorkerA 'server/index.json') -Value '{"latest":"capture-a"}' -Encoding utf8NoBOM
    Invoke-TestGit -C $catalogWorkerA add server
    Invoke-TestGit -C $catalogWorkerA commit -m capture-a

    New-Item -ItemType Directory -Path (Join-Path $catalogWorkerB 'server/capture-b') | Out-Null
    Set-Content -LiteralPath (Join-Path $catalogWorkerB 'server/capture-b/data.txt') -Value capture-b -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $catalogWorkerB 'server/LATEST.txt') -Value capture-b -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $catalogWorkerB 'server/index.json') -Value '{"latest":"capture-b"}' -Encoding utf8NoBOM
    Invoke-TestGit -C $catalogWorkerB add server
    Invoke-TestGit -C $catalogWorkerB commit -m capture-b
    Invoke-TestGit -C $catalogWorkerB push origin HEAD:main

    Invoke-TestGit -C $catalogWorkerA fetch origin '+refs/heads/main:refs/remotes/origin/main'
    Invoke-BackupPublicationRebase -Checkout $catalogWorkerA -Upstream origin/main -GeneratedCatalogPaths @('server/LATEST.txt', 'server/index.json')
    foreach ($requiredPath in @('server/capture-a/data.txt', 'server/capture-b/data.txt')) {
        & git -C $catalogWorkerA cat-file -e "HEAD:$requiredPath"
        if ($LASTEXITCODE -ne 0) {
            throw "Catalog-only conflict resolution lost $requiredPath."
        }
    }
    if ((Get-Content -LiteralPath (Join-Path $catalogWorkerA 'server/LATEST.txt') -Raw).Trim() -ne 'capture-b') {
        throw 'Catalog-only conflict resolution did not preserve the refreshed upstream catalog.'
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
