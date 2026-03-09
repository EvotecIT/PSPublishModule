#requires -Version 5.1
[CmdletBinding()]
param(
    [switch]$Force
)
$ErrorActionPreference = 'Stop'

$serviceName = '{{ServiceName}}'

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force:$Force.IsPresent -ErrorAction SilentlyContinue
    }
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 1
}

Get-Service -Name $serviceName -ErrorAction SilentlyContinue
