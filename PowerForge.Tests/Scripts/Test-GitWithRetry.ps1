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
    if ($exitCode -ne 0) {
        New-Item -ItemType Directory -Path $resetPath | Out-Null
    }
    $global:LASTEXITCODE = $exitCode
}

try {
    New-Item -ItemType Directory -Path $resetPath -Force | Out-Null
    $script:attempt = 0
    $script:exitCodes = @(128, 128, 0)
    Invoke-GitWithRetry -Operation 'Transient clone' -Arguments $script:expectedArguments -ResetPath $resetPath -MaxAttempts 3 -RetryDelaySeconds 0
    if ($script:attempt -ne 3 -or (Test-Path -LiteralPath $resetPath)) {
        throw 'Transient clone retry did not recover cleanly on the third attempt.'
    }

    New-Item -ItemType Directory -Path $resetPath -Force | Out-Null
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
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
