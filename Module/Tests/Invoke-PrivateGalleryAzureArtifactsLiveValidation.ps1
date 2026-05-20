<#
.SYNOPSIS
Runs the opt-in live Azure Artifacts private gallery validation flow.

.DESCRIPTION
This helper wraps PrivateGallery.AzureArtifacts.Live.Tests.ps1 with parameters
so desktop support, release operators, and maintainers can prove the managed
Initialize-ModuleRepository onboarding flow without hand-setting environment
variables. It restores the caller's environment variables after the run.

.EXAMPLE
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA
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
    'PSPUBLISHMODULE_TEST_MANIFEST_PATH'
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

    if ($PassThru.IsPresent) {
        $pesterParameters.PassThru = $true
    }

    Invoke-Pester @pesterParameters
} finally {
    foreach ($name in $envNames) {
        if ($null -eq $previous[$name]) {
            [Environment]::SetEnvironmentVariable($name, $null, 'Process')
        } else {
            [Environment]::SetEnvironmentVariable($name, [string] $previous[$name], 'Process')
        }
    }
}
