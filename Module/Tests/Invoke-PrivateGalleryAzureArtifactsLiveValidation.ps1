<#
.SYNOPSIS
Runs the opt-in live Azure Artifacts private gallery validation flow.

.DESCRIPTION
This helper wraps PrivateGallery.AzureArtifacts.Live.Tests.ps1 with parameters
so desktop support, release operators, and maintainers can prove the managed
Initialize-ModuleRepository onboarding flow without hand-setting environment
variables. It restores the caller's environment variables after the run.
The helper fails the script when the live Pester run reports failed tests.

.EXAMPLE
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA `
    -EvidenceFile .\private-gallery-live.evidence.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Organization,

    [Parameter()]
    [string] $Project,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Feed,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $ModuleName,

    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $ProfileName = 'LiveAzureArtifacts',

    [Parameter()]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string] $PublishPackagePath,

    [Parameter()]
    [ValidateSet('Detailed', 'Diagnostic', 'Normal', 'Minimal', 'None')]
    [string] $Output = 'Detailed',

    [Parameter()]
    [ValidateSet('NUnitXml', 'JUnitXml')]
    [string] $OutputFormat = 'NUnitXml',

    [Parameter()]
    [string] $OutputFile,

    [Parameter()]
    [string] $EvidenceFile,

    [Parameter()]
    [string] $ModuleManifestPath,

    [Parameter()]
    [switch] $PassThru
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$testPath = Join-Path -Path $PSScriptRoot -ChildPath 'PrivateGallery.AzureArtifacts.Live.Tests.ps1'
$envNames = @(
    'PSPUBLISHMODULE_AZDO_LIVE',
    'PSPUBLISHMODULE_AZDO_ORGANIZATION',
    'PSPUBLISHMODULE_AZDO_PROJECT',
    'PSPUBLISHMODULE_AZDO_FEED',
    'PSPUBLISHMODULE_AZDO_MODULE_NAME',
    'PSPUBLISHMODULE_AZDO_PROFILE_NAME',
    'PSPUBLISHMODULE_AZDO_PUBLISH_LIVE',
    'PSPUBLISHMODULE_AZDO_PACKAGE_PATH',
    'PSPUBLISHMODULE_TEST_MANIFEST_PATH',
    'POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH'
)
$previous = @{}

foreach ($name in $envNames) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    $env:PSPUBLISHMODULE_AZDO_LIVE = '1'
    $env:PSPUBLISHMODULE_AZDO_ORGANIZATION = $Organization
    $env:PSPUBLISHMODULE_AZDO_FEED = $Feed
    $env:PSPUBLISHMODULE_AZDO_MODULE_NAME = $ModuleName
    $env:PSPUBLISHMODULE_AZDO_PROFILE_NAME = $ProfileName

    if ($PSBoundParameters.ContainsKey('Project') -and -not [string]::IsNullOrWhiteSpace($Project)) {
        $env:PSPUBLISHMODULE_AZDO_PROJECT = $Project
    } else {
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_PROJECT -ErrorAction SilentlyContinue
    }

    if ($PSBoundParameters.ContainsKey('PublishPackagePath') -and -not [string]::IsNullOrWhiteSpace($PublishPackagePath)) {
        $env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE = '1'
        $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH = (Resolve-Path -LiteralPath $PublishPackagePath).Path
    } else {
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_PUBLISH_LIVE -ErrorAction SilentlyContinue
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_PACKAGE_PATH -ErrorAction SilentlyContinue
    }

    if ($PSBoundParameters.ContainsKey('ModuleManifestPath') -and -not [string]::IsNullOrWhiteSpace($ModuleManifestPath)) {
        $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH = (Resolve-Path -LiteralPath $ModuleManifestPath).Path
    } else {
        Remove-Item Env:\PSPUBLISHMODULE_TEST_MANIFEST_PATH -ErrorAction SilentlyContinue
    }

    $pesterParameters = @{
        Path   = $testPath
        Output = $Output
    }

    if ($PSBoundParameters.ContainsKey('OutputFile') -and -not [string]::IsNullOrWhiteSpace($OutputFile)) {
        $outputDirectory = Split-Path -Path $OutputFile -Parent
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        $pesterParameters.OutputFile = $OutputFile
        $pesterParameters.OutputFormat = $OutputFormat
    }

    $pesterParameters.PassThru = $true

    $pesterResult = Invoke-Pester @pesterParameters
    $failedCount = 0
    $passedCount = 0
    $skippedCount = 0
    $totalCount = 0
    $resultText = 'Unknown'
    $durationMilliseconds = $null
    if ($null -ne $pesterResult) {
        $failedCountProperty = $pesterResult.PSObject.Properties['FailedCount']
        if ($failedCountProperty -and $null -ne $failedCountProperty.Value) {
            $failedCount = [int] $failedCountProperty.Value
        }

        $passedCountProperty = $pesterResult.PSObject.Properties['PassedCount']
        if ($passedCountProperty -and $null -ne $passedCountProperty.Value) {
            $passedCount = [int] $passedCountProperty.Value
        }

        $skippedCountProperty = $pesterResult.PSObject.Properties['SkippedCount']
        if ($skippedCountProperty -and $null -ne $skippedCountProperty.Value) {
            $skippedCount = [int] $skippedCountProperty.Value
        }

        $totalCountProperty = $pesterResult.PSObject.Properties['TotalCount']
        if ($totalCountProperty -and $null -ne $totalCountProperty.Value) {
            $totalCount = [int] $totalCountProperty.Value
        }

        $resultProperty = $pesterResult.PSObject.Properties['Result']
        if ($resultProperty -and $null -ne $resultProperty.Value) {
            $resultText = [string] $resultProperty.Value
        }

        if ($failedCount -eq 0 -and $resultText -eq 'Failed') {
            $failedCount = 1
        }

        $durationProperty = $pesterResult.PSObject.Properties['Duration']
        if ($durationProperty -and $durationProperty.Value -is [TimeSpan]) {
            $durationMilliseconds = [math]::Round($durationProperty.Value.TotalMilliseconds, 0)
        }
    }

    if ($PSBoundParameters.ContainsKey('EvidenceFile') -and -not [string]::IsNullOrWhiteSpace($EvidenceFile)) {
        $evidenceDirectory = Split-Path -Path $EvidenceFile -Parent
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory) -and -not (Test-Path -LiteralPath $evidenceDirectory)) {
            New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
        }

        $evidence = [ordered]@{
            SchemaVersion        = 1
            GeneratedAtUtc       = [DateTimeOffset]::UtcNow.ToString('o')
            Succeeded            = $failedCount -eq 0 -and $resultText -ne 'Failed'
            Provider             = 'AzureArtifacts'
            Organization         = $Organization
            Project              = if ($PSBoundParameters.ContainsKey('Project') -and -not [string]::IsNullOrWhiteSpace($Project)) { $Project } else { $null }
            Feed                 = $Feed
            ModuleName           = $ModuleName
            ProfileName          = $ProfileName
            PublishPackageSupplied = $PSBoundParameters.ContainsKey('PublishPackagePath') -and -not [string]::IsNullOrWhiteSpace($PublishPackagePath)
            PublishPackageName   = if ($PSBoundParameters.ContainsKey('PublishPackagePath') -and -not [string]::IsNullOrWhiteSpace($PublishPackagePath)) { [IO.Path]::GetFileName($PublishPackagePath) } else { $null }
            Pester               = [ordered]@{
                Result               = $resultText
                TotalCount           = $totalCount
                PassedCount          = $passedCount
                FailedCount          = $failedCount
                SkippedCount         = $skippedCount
                DurationMilliseconds = $durationMilliseconds
                OutputFile           = if ($PSBoundParameters.ContainsKey('OutputFile') -and -not [string]::IsNullOrWhiteSpace($OutputFile)) { $OutputFile } else { $null }
                OutputFormat         = if ($PSBoundParameters.ContainsKey('OutputFile') -and -not [string]::IsNullOrWhiteSpace($OutputFile)) { $OutputFormat } else { $null }
            }
        }

        $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidenceFile -Encoding UTF8
    }

    if ($failedCount -gt 0) {
        throw "Live Azure Artifacts private gallery validation failed: $failedCount Pester test(s) failed."
    }

    if ($PassThru.IsPresent) {
        $pesterResult
    }
} finally {
    foreach ($name in $envNames) {
        if ($null -eq $previous[$name]) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        } else {
            [Environment]::SetEnvironmentVariable($name, [string] $previous[$name], 'Process')
        }
    }
}
