#requires -Version 5.1
<#
.SYNOPSIS
    Installs a packaged service or updates its existing registration in place.
.DESCRIPTION
    Validates the new executable before changing registration. Existing service accounts and
    startup modes are retained unless an account change is explicitly requested.
.PARAMETER ServiceName
    Windows service name.
.PARAMETER DisplayName
    Service display name.
.PARAMETER Description
    Service description.
.PARAMETER ConfigPath
    Configuration path for a new registration, or a fallback when preserved arguments have no config option.
.PARAMETER Account
    Explicit account change, or the account for a new service. New services default to LocalSystem.
.PARAMETER Password
    Password for an explicitly selected account.
.PARAMETER BackupPath
    Protected ImagePath backup supplied by the MSI upgrade workflow.
.PARAMETER UpgradeMode
    Enables upgrade behavior and validation.
.PARAMETER PreserveExistingServiceBinPath
    Retains the complete command line from the required protected backup.
.PARAMETER PreserveExistingServiceArguments
    Uses the new executable with the existing arguments and active configuration option.
.PARAMETER Start
    Starts the service after registration succeeds.
.EXAMPLE
    .\Install-Service.ps1 -UpgradeMode -PreserveExistingServiceArguments -Start
.OUTPUTS
    System.ServiceProcess.ServiceController
#>
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
    [switch]$PreserveExistingServiceArguments,
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
    $pattern = '(?i)(^|\s)' + [regex]::Escape($Name) + '(?:\s+|=)(".*?"|\S+)'
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
$service = $null
if ($existing) {
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$($ServiceName.Replace("'", "''"))'"
    if ($null -eq $service) {
        throw "Unable to resolve existing service '$ServiceName' for an in-place update."
    }
}
if ($PreserveExistingServiceArguments -and $PreserveExistingServiceBinPath) {
    throw 'Preserve either the existing arguments or the entire command line, not both.'
}
if ($UpgradeMode -and $PreserveExistingServiceArguments -and -not $service) {
    throw 'The existing service registration is missing; its account cannot be preserved safely.'
}
if ($PreserveExistingServiceArguments -and $service) {
    $previousCommand = $service.PathName
    if ($UpgradeMode -and -not [string]::IsNullOrWhiteSpace($BackupPath)) {
        $previousCommand = (Get-Content -LiteralPath $BackupPath -Raw -ErrorAction Stop).Trim()
    }
    $match = [regex]::Match($previousCommand, '^\s*(?:"[^"\r\n]+\.exe"|[^"\r\n]+?\.exe)(?<arguments>\s.*)?$', 'IgnoreCase')
    if (-not $match.Success) {
        throw 'The existing service executable and arguments could not be resolved safely.'
    }
    $binaryPathName = '"' + $exePath + '"' + $match.Groups['arguments'].Value
}
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

$hasPreservedConfig = $PreserveExistingServiceArguments -and
    $binaryPathName -match '(?i)(?:^|\s)--config(?:-path)?(?:\s|=)'
if (-not [string]::IsNullOrWhiteSpace($ConfigPath) -and -not $hasPreservedConfig) {
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
if ($Account -eq 'NT AUTHORITY\SYSTEM') { $Account = 'LocalSystem' }
if ($Account -eq 'LocalService') { $Account = 'NT AUTHORITY\LocalService' }
if ($Account -eq 'NetworkService') { $Account = 'NT AUTHORITY\NetworkService' }
$passwordlessAccount = $Account -in @('LocalSystem', 'NT AUTHORITY\SYSTEM', 'LocalService', 'NT AUTHORITY\LocalService', 'NetworkService', 'NT AUTHORITY\NetworkService') -or $Account.EndsWith('$')
$changeAccount = $PSBoundParameters.ContainsKey('Account') -and
    ($null -eq $service -or $Account -ne $service.StartName -or $PSBoundParameters.ContainsKey('Password'))
$needsCredential = ($null -eq $service -or $changeAccount) -and
    -not [string]::IsNullOrWhiteSpace($Account) -and -not $passwordlessAccount
if ($needsCredential) {
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
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }

    $changeArguments = @{
        PathName    = $binaryPathName
        DisplayName = $DisplayName
    }
    if ($changeAccount) {
        $changeArguments.StartName = $Account
    }
    if ($changeAccount -and -not [string]::IsNullOrWhiteSpace($servicePassword)) {
        $changeArguments.StartPassword = $servicePassword
    }
    if ($changeAccount -and $passwordlessAccount) { $changeArguments.StartPassword = '' }

    $changeResult = Invoke-CimMethod -InputObject $service -MethodName Change -Arguments $changeArguments
    if ($null -eq $changeResult -or $changeResult.ReturnValue -ne 0) {
        $returnValue = if ($null -eq $changeResult) { 'unknown' } else { $changeResult.ReturnValue }
        throw "Failed to update existing service '$ServiceName' (Win32_Service.Change=$returnValue)."
    }

    Set-Service -Name $ServiceName -DisplayName $DisplayName -Description $Description
} else {
    if ($passwordlessAccount -and $Account -notin @('LocalSystem', 'NT AUTHORITY\SYSTEM')) {
        $createArguments = @{
            Name=$ServiceName; DisplayName=$DisplayName; PathName=$binaryPathName
            ServiceType=[byte]16; ErrorControl=[byte]1; StartMode='Automatic'
            DesktopInteract=$false; StartName=$Account; StartPassword=''
        }
        $createResult = Invoke-CimMethod -ClassName Win32_Service -MethodName Create -Arguments $createArguments
        if ($null -eq $createResult -or $createResult.ReturnValue -ne 0) {
            throw "Failed to create passwordless service '$ServiceName' (Win32_Service.Create=$($createResult.ReturnValue))."
        }
        Set-Service -Name $ServiceName -Description $Description
    } else {
        New-Service @newServiceParams | Out-Null
    }
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
    if ($service -and $service.StartMode -eq 'Disabled') {
        Write-Verbose 'The existing disabled startup mode is retained; the service remains stopped.'
    } else {
        Start-Service -Name $ServiceName
    }
}

Get-Service -Name $ServiceName
