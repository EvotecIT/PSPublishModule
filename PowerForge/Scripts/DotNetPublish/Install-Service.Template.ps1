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
$recoveryConfigured = {{RecoveryConfigured}}
$recoveryEnabled = {{RecoveryEnabled}}
$recoveryResetPeriodSeconds = {{RecoveryResetPeriodSeconds}}
$recoveryRestartDelayMilliseconds = @({{RecoveryRestartDelayMilliseconds}})
$recoveryApplyToNonCrashFailures = {{RecoveryApplyToNonCrashFailures}}
$recoveryFailureMode = '{{RecoveryFailureMode}}'

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

function Invoke-ServiceRecoveryCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$Operation
    )

    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    $message = "Failed to $Operation for service '$ServiceName' (sc.exe exit $LASTEXITCODE). $($output -join ' ')"
    switch ($recoveryFailureMode) {
        'Fail' { throw $message }
        'Warn' { Write-Warning $message }
        'Skip' { }
        default { Write-Warning $message }
    }
    return $false
}

$binaryPathName = '"' + $exePath + '"'
if (-not [string]::IsNullOrWhiteSpace($arguments)) {
    $binaryPathName += ' ' + $arguments
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($UpgradeMode -and $PreserveExistingServiceBinPath) {
    if ([string]::IsNullOrWhiteSpace($BackupPath) -or -not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw 'The existing service command line backup is required for this upgrade.'
    }

    $preservedBinaryPathName = (Get-Content -LiteralPath $BackupPath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($preservedBinaryPathName)) {
        throw 'The existing service command line backup is empty.'
    }

    $binaryPathName = $preservedBinaryPathName
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

$newServiceParams = @{
    Name           = $ServiceName
    BinaryPathName = $binaryPathName
    DisplayName    = $DisplayName
    Description    = $Description
    StartupType    = 'Automatic'
}

$credential = $null
$servicePassword = $Password
if (-not [string]::IsNullOrWhiteSpace($Account) -and $Account -ne 'LocalSystem') {
    if ([string]::IsNullOrWhiteSpace($Password)) {
        $credential = Get-Credential -UserName $Account -Message 'Enter password for service account'
        $servicePassword = $credential.GetNetworkCredential().Password
    } else {
        $securePassword = ConvertTo-SecureString -String $Password -AsPlainText -Force
        $credential = [pscredential]::new($Account, $securePassword)
    }
    $newServiceParams.Credential = $credential
}

if ($existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    }

    $service = Get-CimInstance -ClassName Win32_Service |
        Where-Object Name -EQ $ServiceName |
        Select-Object -First 1
    if ($null -eq $service) {
        throw "Unable to resolve existing service '$ServiceName' for an in-place update."
    }

    $changeArguments = @{
        PathName    = $binaryPathName
        DisplayName = $DisplayName
        StartMode   = 'Automatic'
        StartName   = $Account
    }
    if (-not [string]::IsNullOrWhiteSpace($servicePassword)) {
        $changeArguments.StartPassword = $servicePassword
    }

    $changeResult = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments $changeArguments
    if ($null -eq $changeResult -or $changeResult.ReturnValue -ne 0) {
        $returnValue = if ($null -eq $changeResult) { 'unknown' } else { $changeResult.ReturnValue }
        throw "Failed to update existing service '$ServiceName' (Win32_Service.Change=$returnValue)."
    }

    Set-Service -Name $ServiceName -DisplayName $DisplayName -Description $Description -StartupType Automatic
} else {
    New-Service @newServiceParams | Out-Null
}

if ($recoveryConfigured) {
    $recoveryActions = if ($recoveryEnabled) {
        ($recoveryRestartDelayMilliseconds | ForEach-Object { "restart/$_" }) -join '/'
    } else {
        # Windows PowerShell 5.1 drops empty native arguments. The quoted payload
        # preserves the required empty value for `sc.exe failure ... actions= ""`.
        '""'
    }
    $recoveryActionsConfigured = Invoke-ServiceRecoveryCommand `
        -Arguments @('failure', $ServiceName, 'reset=', [string]$recoveryResetPeriodSeconds, 'actions=', $recoveryActions) `
        -Operation $(if ($recoveryEnabled) { 'configure recovery actions' } else { 'clear recovery actions' })
    if ($recoveryActionsConfigured) {
        [void](Invoke-ServiceRecoveryCommand `
            -Arguments @('failureflag', $ServiceName, $(if ($recoveryEnabled -and $recoveryApplyToNonCrashFailures) { '1' } else { '0' })) `
            -Operation 'configure the recovery failure flag')
    }
}

if ($Start) {
    Start-Service -Name $ServiceName
}

Get-Service -Name $ServiceName
