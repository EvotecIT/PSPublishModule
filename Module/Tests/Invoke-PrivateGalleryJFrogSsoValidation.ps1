<#
.SYNOPSIS
Runs an opt-in JFrog private gallery SSO bridge validation.

.DESCRIPTION
This helper is intended for an interactive workstation that can reach a real
JFrog Artifactory instance. It gathers non-secret evidence for three questions:

1. Is JFrog CLI present and can an operator complete jf login?
2. After JFrog CLI login, can PSResourceGet read the JFrog NuGet feed without
   explicit credentials?
3. If supplied, does the PSPublishModule credential/token fallback path work?

The evidence JSON is safe to share in issues or pull requests. It does not
write credential values.

.EXAMPLE
.\Module\Tests\Invoke-PrivateGalleryJFrogSsoValidation.ps1 `
    -JFrogBaseUri https://company.jfrog.io/artifactory `
    -Repository powershell-virtual `
    -ModuleName PSPublishModule `
    -RunJFrogCliLogin `
    -EvidenceFile .\jfrog-sso.evidence.json `
    -MarkdownFile .\jfrog-sso.evidence.md

.EXAMPLE
.\Module\Tests\Invoke-PrivateGalleryJFrogSsoValidation.ps1 `
    -JFrogBaseUri https://company.jfrog.io/artifactory `
    -Repository powershell-virtual `
    -ModuleName PSPublishModule `
    -CredentialUserName user@example.com `
    -CredentialSecretFilePath .\jfrog-token.txt `
    -EvidenceFile .\jfrog-token.evidence.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $JFrogBaseUri,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Repository,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ModuleName,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ProfileName = 'LiveJFrog',

    [Parameter()]
    [string] $RepositoryName,

    [Parameter()]
    [string] $CredentialUserName,

    [Parameter()]
    [string] $CredentialSecret,

    [Parameter()]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $CredentialSecretFilePath,

    [Parameter()]
    [switch] $RunJFrogCliLogin,

    [Parameter()]
    [switch] $InstallModule,

    [Parameter()]
    [switch] $KeepRepository,

    [Parameter()]
    [string] $EvidenceFile,

    [Parameter()]
    [string] $MarkdownFile,

    [Parameter()]
    [string] $ModuleManifestPath,

    [Parameter()]
    [switch] $PassThru
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$items = [System.Collections.Generic.List[object]]::new()
$errors = [System.Collections.Generic.List[string]]::new()
$registeredRepository = $false
$moduleImported = $false
$normalizedBaseUri = $JFrogBaseUri.Trim().TrimEnd('/')
$repositoryKey = $Repository.Trim().Trim('/')
$localRepositoryName = if ([string]::IsNullOrWhiteSpace($RepositoryName)) { $ProfileName } else { $RepositoryName.Trim() }
$psResourceGetUri = "$normalizedBaseUri/api/nuget/v3/$([Uri]::EscapeDataString($repositoryKey))/index.json"
$powerShellGetUri = "$normalizedBaseUri/api/nuget/$([Uri]::EscapeDataString($repositoryKey))"

function Add-EvidenceItem {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [bool] $Succeeded,

        [Parameter()]
        [hashtable] $Details
    )

    $ordered = [ordered]@{
        Name      = $Name
        Succeeded = $Succeeded
    }

    if ($Details) {
        foreach ($key in $Details.Keys) {
            $ordered[$key] = $Details[$key]
        }
    }

    $items.Add([pscustomobject] $ordered)
}

function ConvertTo-ValidationMessage {
    param(
        [Parameter(Mandatory)]
        [object] $ErrorRecord
    )

    $message = if ($ErrorRecord -is [System.Management.Automation.ErrorRecord]) {
        $ErrorRecord.Exception.Message
    } else {
        [string] $ErrorRecord
    }

    return $message.Replace("`r", ' ').Replace("`n", ' ').Trim()
}

function Invoke-CapturedNativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [string[]] $ArgumentList = @()
    )

    $output = @(& $FilePath @ArgumentList 2>&1)
    [pscustomobject]@{
        ExitCode = $global:LASTEXITCODE
        Output   = (($output | ForEach-Object { [string] $_ }) -join [Environment]::NewLine)
    }
}

function Get-ModuleBinaryPath {
    $releaseRoot = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..\..') -ChildPath 'PSPublishModule\bin\Release'
    if (-not (Test-Path -LiteralPath $releaseRoot -PathType Container)) {
        return $null
    }

    $candidateFrameworks = Get-ChildItem -LiteralPath $releaseRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            if ($PSVersionTable.PSEdition -eq 'Desktop') {
                $_.Name -eq 'net472'
            } else {
                $_.Name -match '^net\d'
            }
        } |
        Sort-Object -Property Name -Descending

    foreach ($framework in $candidateFrameworks) {
        $path = Join-Path -Path $framework.FullName -ChildPath 'PSPublishModule.dll'
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            return $path
        }
    }

    return $null
}

function Import-ValidationModule {
    if ($PSBoundParameters.ContainsKey('ModuleManifestPath') -and -not [string]::IsNullOrWhiteSpace($ModuleManifestPath)) {
        Import-Module (Resolve-Path -LiteralPath $ModuleManifestPath).Path -Force -ErrorAction Stop
        return
    }

    $loaded = Get-Module PSPublishModule -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($loaded) {
        return
    }

    $binary = Get-ModuleBinaryPath
    if ($binary) {
        Import-Module $binary -Force -ErrorAction Stop
        return
    }

    $manifest = Join-Path -Path (Join-Path -Path $PSScriptRoot -ChildPath '..') -ChildPath 'PSPublishModule.psd1'
    Import-Module $manifest -Force -ErrorAction Stop
}

function Get-CommandVersionText {
    param(
        [Parameter(Mandatory)]
        [string] $CommandName
    )

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $command) {
        return $null
    }

    try {
        if ($CommandName -eq 'jf') {
            $version = Invoke-CapturedNativeCommand -FilePath $command.Source -ArgumentList @('--version')
            return ($version.Output -split "`r?`n" | Select-Object -First 1)
        }

        $module = Get-Module -ListAvailable -Name $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($module) {
            return [string] $module.Version
        }
    } catch {
        return ConvertTo-ValidationMessage $_
    }

    return $null
}

function Unregister-ValidationRepository {
    param(
        [Parameter(Mandatory)]
        [string] $Name
    )

    if (Get-Command Unregister-PSResourceRepository -ErrorAction SilentlyContinue) {
        Unregister-PSResourceRepository -Name $Name -ErrorAction SilentlyContinue
    }

    if (Get-Command Unregister-PSRepository -ErrorAction SilentlyContinue) {
        Unregister-PSRepository -Name $Name -ErrorAction SilentlyContinue
    }
}

function Write-MarkdownEvidence {
    param(
        [Parameter(Mandatory)]
        [object] $Evidence,

        [Parameter(Mandatory)]
        [string] $Path
    )

    function ConvertTo-MarkdownValue {
        param([AllowNull()] [object] $Value)
        if ($null -eq $Value) { return '' }
        return ([string] $Value).Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('### JFrog Private Gallery Validation')
    $lines.Add('')
    $lines.Add('| Field | Value |')
    $lines.Add('| --- | --- |')
    $lines.Add("| Succeeded | $(ConvertTo-MarkdownValue $Evidence.Succeeded) |")
    $lines.Add("| JFrog base URI | $(ConvertTo-MarkdownValue $Evidence.JFrogBaseUri) |")
    $lines.Add("| Repository | $(ConvertTo-MarkdownValue $Evidence.Repository) |")
    $lines.Add("| Module | $(ConvertTo-MarkdownValue $Evidence.ModuleName) |")
    $lines.Add("| Profile | $(ConvertTo-MarkdownValue $Evidence.ProfileName) |")
    $lines.Add("| JFrog CLI version | $(ConvertTo-MarkdownValue $Evidence.Tooling.JFrogCliVersion) |")
    $lines.Add("| PSResourceGet version | $(ConvertTo-MarkdownValue $Evidence.Tooling.PSResourceGetVersion) |")
    $lines.Add('')
    $lines.Add('| Validation item | Succeeded | Details |')
    $lines.Add('| --- | --- | --- |')

    foreach ($item in @($Evidence.ValidationItems)) {
        $details = @()
        foreach ($property in $item.PSObject.Properties) {
            if ($property.Name -in @('Name', 'Succeeded')) {
                continue
            }

            if ($null -ne $property.Value -and -not [string]::IsNullOrWhiteSpace([string] $property.Value)) {
                $details += "$($property.Name)=$($property.Value)"
            }
        }

        $lines.Add("| $(ConvertTo-MarkdownValue $item.Name) | $(ConvertTo-MarkdownValue $item.Succeeded) | $(ConvertTo-MarkdownValue ($details -join ', ')) |")
    }

    if (@($Evidence.Errors).Count -gt 0) {
        $lines.Add('')
        $lines.Add('| Error |')
        $lines.Add('| --- |')
        foreach ($errorItem in @($Evidence.Errors)) {
            $lines.Add("| $(ConvertTo-MarkdownValue $errorItem) |")
        }
    }

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $lines -join [Environment]::NewLine | Set-Content -LiteralPath $Path -Encoding UTF8
}

try {
    Import-ValidationModule
    $moduleImported = $true
    Add-EvidenceItem -Name 'ImportModule' -Succeeded $true -Details @{
        ModuleImported = $true
    }
} catch {
    $message = ConvertTo-ValidationMessage $_
    $errors.Add("ImportModule: $message")
    Add-EvidenceItem -Name 'ImportModule' -Succeeded $false -Details @{
        Error = $message
    }
}

$jfCommand = Get-Command jf -ErrorAction SilentlyContinue | Select-Object -First 1
if ($jfCommand) {
    Add-EvidenceItem -Name 'DetectJFrogCli' -Succeeded $true -Details @{
        Path    = $jfCommand.Source
        Version = Get-CommandVersionText -CommandName 'jf'
    }
} else {
    Add-EvidenceItem -Name 'DetectJFrogCli' -Succeeded $false -Details @{
        Error = 'jf was not found on PATH.'
    }
}

if ($RunJFrogCliLogin.IsPresent) {
    if (-not $jfCommand) {
        $errors.Add('JFrog CLI login was requested, but jf was not found on PATH.')
        Add-EvidenceItem -Name 'JFrogCliLogin' -Succeeded $false -Details @{
            Error = 'jf was not found on PATH.'
        }
    } else {
        try {
            $login = Invoke-CapturedNativeCommand -FilePath $jfCommand.Source -ArgumentList @('login')
            $succeeded = $login.ExitCode -eq 0
            if (-not $succeeded) {
                $errors.Add("JFrogCliLogin: jf login returned exit code $($login.ExitCode).")
            }

            Add-EvidenceItem -Name 'JFrogCliLogin' -Succeeded $succeeded -Details @{
                ExitCode = $login.ExitCode
                Output   = $login.Output
            }
        } catch {
            $message = ConvertTo-ValidationMessage $_
            $errors.Add("JFrogCliLogin: $message")
            Add-EvidenceItem -Name 'JFrogCliLogin' -Succeeded $false -Details @{
                Error = $message
            }
        }
    }
}

try {
    if (-not (Get-Command Register-PSResourceRepository -ErrorAction SilentlyContinue)) {
        throw 'Register-PSResourceRepository was not found. Install Microsoft.PowerShell.PSResourceGet before running this validation.'
    }

    Unregister-ValidationRepository -Name $localRepositoryName
    Register-PSResourceRepository -Name $localRepositoryName -Uri $psResourceGetUri -Trusted -Priority 40 -ErrorAction Stop
    $registeredRepository = $true
    Add-EvidenceItem -Name 'RegisterPSResourceRepository' -Succeeded $true -Details @{
        RepositoryName = $localRepositoryName
        Uri            = $psResourceGetUri
        Priority       = 40
    }
} catch {
    $message = ConvertTo-ValidationMessage $_
    $errors.Add("RegisterPSResourceRepository: $message")
    Add-EvidenceItem -Name 'RegisterPSResourceRepository' -Succeeded $false -Details @{
        Error = $message
    }
}

if ($registeredRepository) {
    try {
        $found = @(Find-PSResource -Name $ModuleName -Repository $localRepositoryName -ErrorAction Stop)
        Add-EvidenceItem -Name 'NoCredentialPSResourceGetFind' -Succeeded ($found.Count -gt 0) -Details @{
            FoundCount = $found.Count
            Versions   = (($found | Select-Object -ExpandProperty Version -ErrorAction SilentlyContinue) -join ', ')
        }
        if ($found.Count -eq 0) {
            $errors.Add("NoCredentialPSResourceGetFind: module '$ModuleName' was not returned from '$localRepositoryName'.")
        }
    } catch {
        $message = ConvertTo-ValidationMessage $_
        $errors.Add("NoCredentialPSResourceGetFind: $message")
        Add-EvidenceItem -Name 'NoCredentialPSResourceGetFind' -Succeeded $false -Details @{
            Error = $message
        }
    }

    if ($InstallModule.IsPresent) {
        try {
            Install-PSResource -Name $ModuleName -Repository $localRepositoryName -TrustRepository -Scope CurrentUser -ErrorAction Stop
            Add-EvidenceItem -Name 'NoCredentialPSResourceGetInstall' -Succeeded $true -Details @{
                ModuleName = $ModuleName
            }
        } catch {
            $message = ConvertTo-ValidationMessage $_
            $errors.Add("NoCredentialPSResourceGetInstall: $message")
            Add-EvidenceItem -Name 'NoCredentialPSResourceGetInstall' -Succeeded $false -Details @{
                Error = $message
            }
        }
    }
}

$hasCredentialSecret = -not [string]::IsNullOrWhiteSpace($CredentialSecret) -or
    -not [string]::IsNullOrWhiteSpace($CredentialSecretFilePath)
$hasCredential = -not [string]::IsNullOrWhiteSpace($CredentialUserName) -and $hasCredentialSecret

if ($moduleImported -and $hasCredential) {
    try {
        $setProfile = @{
            Name          = $ProfileName
            Provider      = 'JFrog'
            Repository    = $repositoryKey
            JFrogBaseUri  = $normalizedBaseUri
            RepositoryName = $localRepositoryName
        }

        $profile = Set-ManagedModuleRepository @setProfile
        $profile | Out-Null

        $connect = @{
            ProfileName        = $ProfileName
            CredentialUserName = $CredentialUserName
            BootstrapMode      = 'CredentialPrompt'
            ErrorAction        = 'Stop'
        }
        if (-not [string]::IsNullOrWhiteSpace($CredentialSecretFilePath)) {
            $connect.CredentialSecretFilePath = $CredentialSecretFilePath
        } else {
            $connect.CredentialSecret = $CredentialSecret
        }

        $connection = Initialize-ManagedModuleRepository @connect
        Add-EvidenceItem -Name 'CredentialConnectModuleRepository' -Succeeded ([bool] $connection.AccessProbeSucceeded) -Details @{
            RepositoryName        = $connection.RepositoryName
            AccessProbePerformed  = $connection.AccessProbePerformed
            AccessProbeSucceeded  = $connection.AccessProbeSucceeded
            AccessProbeTool       = $connection.AccessProbeTool
            BootstrapModeUsed     = [string] $connection.BootstrapModeUsed
            CredentialSource      = [string] $connection.CredentialSource
        }

        if (-not $connection.AccessProbeSucceeded) {
            $errors.Add("CredentialConnectModuleRepository: $($connection.AccessProbeMessage)")
        }
    } catch {
        $message = ConvertTo-ValidationMessage $_
        $errors.Add("CredentialConnectModuleRepository: $message")
        Add-EvidenceItem -Name 'CredentialConnectModuleRepository' -Succeeded $false -Details @{
            Error = $message
        }
    }
} elseif (-not $hasCredential) {
    Add-EvidenceItem -Name 'CredentialConnectModuleRepository' -Succeeded $false -Details @{
        Skipped = 'CredentialUserName plus CredentialSecret/CredentialSecretFilePath were not supplied.'
    }
}

$succeeded = $errors.Count -eq 0
$evidence = [ordered]@{
    SchemaVersion   = 1
    GeneratedAtUtc  = [DateTimeOffset]::UtcNow.ToString('o')
    Succeeded       = $succeeded
    Provider        = 'JFrog'
    JFrogBaseUri    = $normalizedBaseUri
    Repository      = $repositoryKey
    RepositoryName  = $localRepositoryName
    PSResourceGetUri = $psResourceGetUri
    PowerShellGetUri = $powerShellGetUri
    ModuleName      = $ModuleName
    ProfileName     = $ProfileName
    RunJFrogCliLogin = $RunJFrogCliLogin.IsPresent
    InstallModule   = $InstallModule.IsPresent
    Tooling         = [ordered]@{
        PowerShellVersion     = [string] $PSVersionTable.PSVersion
        PSEdition             = [string] $PSVersionTable.PSEdition
        JFrogCliDetected      = $null -ne $jfCommand
        JFrogCliPath          = if ($jfCommand) { $jfCommand.Source } else { $null }
        JFrogCliVersion       = Get-CommandVersionText -CommandName 'jf'
        PSResourceGetVersion  = Get-CommandVersionText -CommandName 'Microsoft.PowerShell.PSResourceGet'
        PowerShellGetVersion  = Get-CommandVersionText -CommandName 'PowerShellGet'
    }
    ValidationItems = $items.ToArray()
    Errors          = $errors.ToArray()
}

if ($PSBoundParameters.ContainsKey('EvidenceFile') -and -not [string]::IsNullOrWhiteSpace($EvidenceFile)) {
    $directory = Split-Path -Path $EvidenceFile -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidenceFile -Encoding UTF8
}

if ($PSBoundParameters.ContainsKey('MarkdownFile') -and -not [string]::IsNullOrWhiteSpace($MarkdownFile)) {
    Write-MarkdownEvidence -Evidence ([pscustomobject] $evidence) -Path $MarkdownFile
}

if (-not $KeepRepository.IsPresent) {
    Unregister-ValidationRepository -Name $localRepositoryName
}

if ($PassThru.IsPresent) {
    [pscustomobject] $evidence
}

if (-not $succeeded) {
    throw "JFrog private gallery validation failed. See EvidenceFile/MarkdownFile output for details."
}
