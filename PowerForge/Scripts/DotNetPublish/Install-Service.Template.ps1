#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ServiceName = '{{ServiceName}}',
    [string]$DisplayName = '{{DisplayName}}',
    [string]$Description = '{{Description}}',
    [string]$ConfigPath,
    [string]$Account = 'LocalSystem',
    [string]$Password,
    [string]$BackupPath,
    [switch]$UpgradeMode,
    [switch]$PreserveExistingServiceBinPath,
    [switch]$Start
)
$ErrorActionPreference = 'Stop'

$binaryRelative = '{{ExecutableRelativePath}}'
$arguments = '{{Arguments}}'

$packageRoot = Split-Path -Parent $PSCommandPath
$exePath = Join-Path -Path $packageRoot -ChildPath $binaryRelative

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Service binary not found: $exePath"
}

function Set-CommandLineOption {
    param(
        [Parameter(Mandatory)]
        [string]$CommandLine,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Value
    )

    $escapedValue = $Value.Replace('"', '\"')
    $pattern = '(?i)(^|\s)' + [regex]::Escape($Name) + '\s+(".*?"|\S+)'
    if ($CommandLine -match $pattern) {
        return [regex]::Replace($CommandLine, $pattern, '$1' + $Name + ' "' + $escapedValue + '"', 1)
    }

    return ($CommandLine + ' ' + $Name + ' "' + $escapedValue + '"').Trim()
}

$binaryPathName = '"' + $exePath + '"'
if (-not [string]::IsNullOrWhiteSpace($arguments)) {
    $binaryPathName += ' ' + $arguments
}

if ($UpgradeMode -and $PreserveExistingServiceBinPath -and -not [string]::IsNullOrWhiteSpace($BackupPath) -and (Test-Path -LiteralPath $BackupPath)) {
    $preservedBinaryPathName = (Get-Content -LiteralPath $BackupPath -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($preservedBinaryPathName)) {
        $binaryPathName = $preservedBinaryPathName
    }
}

if (-not [string]::IsNullOrWhiteSpace($ConfigPath)) {
    $resolvedConfigPath = $ConfigPath
    if (-not [System.IO.Path]::IsPathRooted($resolvedConfigPath)) {
        $resolvedConfigPath = Join-Path -Path $packageRoot -ChildPath $resolvedConfigPath
    }

    $binaryPathName = Set-CommandLineOption -CommandLine $binaryPathName -Name '--config' -Value $resolvedConfigPath
}

if (-not [string]::IsNullOrWhiteSpace($ServiceName)) {
    $binaryPathName = Set-CommandLineOption -CommandLine $binaryPathName -Name '--service-name' -Value $ServiceName
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

$newServiceParams = @{
    Name           = $ServiceName
    BinaryPathName = $binaryPathName
    DisplayName    = $DisplayName
    Description    = $Description
    StartupType    = 'Automatic'
}

if (-not [string]::IsNullOrWhiteSpace($Account) -and $Account -ne 'LocalSystem') {
    if ([string]::IsNullOrWhiteSpace($Password)) {
        $credential = Get-Credential -UserName $Account -Message 'Enter password for service account'
    } else {
        $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
        $credential = [pscredential]::new($Account, $securePassword)
    }
    $newServiceParams.Credential = $credential
}

New-Service @newServiceParams | Out-Null

if ($Start) {
    Start-Service -Name $ServiceName
}

Get-Service -Name $ServiceName
