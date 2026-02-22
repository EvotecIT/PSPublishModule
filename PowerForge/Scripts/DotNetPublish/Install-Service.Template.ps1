#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Start
)
$ErrorActionPreference = 'Stop'

$serviceName = '{{ServiceName}}'
$displayName = '{{DisplayName}}'
$description = '{{Description}}'
$binaryRelative = '{{ExecutableRelativePath}}'
$arguments = '{{Arguments}}'

$packageRoot = Split-Path -Parent $PSCommandPath
$exePath = Join-Path -Path $packageRoot -ChildPath $binaryRelative

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Service binary not found: $exePath"
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

$binaryPathName = '"' + $exePath + '"'
if (-not [string]::IsNullOrWhiteSpace($arguments)) {
    $binaryPathName += ' ' + $arguments
}

New-Service -Name $serviceName -BinaryPathName $binaryPathName -DisplayName $displayName -Description $description -StartupType Automatic | Out-Null

if ($Start) {
    Start-Service -Name $serviceName
}

Get-Service -Name $serviceName
