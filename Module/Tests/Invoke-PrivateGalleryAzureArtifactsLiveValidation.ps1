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

.EXAMPLE
.\Module\Tests\Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1 `
    -Organization contoso `
    -Project Platform `
    -Feed Modules `
    -ModuleName ModuleA `
    -GenerateDisposablePackage `
    -EvidenceFile .\private-gallery-live-publish.evidence.json
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
    [switch] $GenerateDisposablePackage,

    [Parameter()]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$')]
    [string] $DisposablePackageName = 'PSPublishModule.PrivateGallery.LiveValidation',

    [Parameter()]
    [ValidatePattern('^\d+\.\d+\.\d+([-.][A-Za-z0-9][A-Za-z0-9.-]*)?$')]
    [string] $DisposablePackageVersion,

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
    'PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH',
    'PSPUBLISHMODULE_TEST_MANIFEST_PATH',
    'POWERFORGE_MODULE_REPOSITORY_PROFILE_PATH'
)
$previous = @{}
$evidenceDataPath = $null
$generatedPackageRoot = $null
$generatedPackagePath = $null
$resolvedPublishPackagePath = $null
$resolvedDisposablePackageVersion = $null

function Test-ValidationItemSucceeded {
    param(
        [Parameter(Mandatory)]
        [object] $Item,

        [Parameter(Mandatory)]
        [string] $PropertyName
    )

    $property = $Item.PSObject.Properties[$PropertyName]
    return $property -and $null -ne $property.Value -and [bool] $property.Value
}

function Get-ValidationItemValue {
    param(
        [Parameter(Mandatory)]
        [object] $Item,

        [Parameter(Mandatory)]
        [string] $PropertyName
    )

    $property = $Item.PSObject.Properties[$PropertyName]
    if ($property) {
        return $property.Value
    }

    return $null
}

function Ensure-ZipArchiveType {
    try {
        [void] [System.IO.Compression.ZipArchive]
        return
    } catch {
        Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop
    }
}

function New-DisposableNuGetPackage {
    param(
        [Parameter(Mandatory)]
        [string] $PackageId,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [Parameter(Mandatory)]
        [string] $OutputDirectory
    )

    if (-not (Test-Path -LiteralPath $OutputDirectory)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }

    Ensure-ZipArchiveType

    $packagePath = Join-Path $OutputDirectory "$PackageId.$PackageVersion.nupkg"
    $nuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>$([Security.SecurityElement]::Escape($PackageId))</id>
    <version>$([Security.SecurityElement]::Escape($PackageVersion))</version>
    <authors>PSPublishModule</authors>
    <description>Disposable Azure Artifacts private gallery live validation package.</description>
    <packageTypes>
      <packageType name="Dependency" />
    </packageTypes>
  </metadata>
</package>
"@

    $fileStream = [IO.File]::Open($packagePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($fileStream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $entry = $archive.CreateEntry("$PackageId.nuspec")
            $entryStream = $entry.Open()
            try {
                $writer = [IO.StreamWriter]::new($entryStream, [Text.UTF8Encoding]::new($false))
                try {
                    $writer.Write($nuspec)
                } finally {
                    $writer.Dispose()
                }
            } finally {
                $entryStream.Dispose()
            }
        } finally {
            $archive.Dispose()
        }
    } finally {
        $fileStream.Dispose()
    }

    return $packagePath
}

foreach ($name in $envNames) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    if ($GenerateDisposablePackage.IsPresent -and
        $PSBoundParameters.ContainsKey('PublishPackagePath') -and
        -not [string]::IsNullOrWhiteSpace($PublishPackagePath)) {
        throw "GenerateDisposablePackage cannot be combined with PublishPackagePath."
    }

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

    if ($GenerateDisposablePackage.IsPresent) {
        $generatedPackageRoot = Join-Path ([IO.Path]::GetTempPath()) ("PSPublishModule.PrivateGallery.GeneratedPackage." + [guid]::NewGuid().ToString('N'))
        $resolvedDisposablePackageVersion = if ($PSBoundParameters.ContainsKey('DisposablePackageVersion') -and -not [string]::IsNullOrWhiteSpace($DisposablePackageVersion)) {
            $DisposablePackageVersion
        } else {
            "0.0.1-live.$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))"
        }

        $generatedPackagePath = New-DisposableNuGetPackage -PackageId $DisposablePackageName -PackageVersion $resolvedDisposablePackageVersion -OutputDirectory $generatedPackageRoot
        $resolvedPublishPackagePath = $generatedPackagePath
        $env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE = '1'
        $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH = $resolvedPublishPackagePath
    } elseif ($PSBoundParameters.ContainsKey('PublishPackagePath') -and -not [string]::IsNullOrWhiteSpace($PublishPackagePath)) {
        $resolvedPublishPackagePath = (Resolve-Path -LiteralPath $PublishPackagePath).Path
        $env:PSPUBLISHMODULE_AZDO_PUBLISH_LIVE = '1'
        $env:PSPUBLISHMODULE_AZDO_PACKAGE_PATH = $resolvedPublishPackagePath
    } else {
        $resolvedPublishPackagePath = $null
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_PUBLISH_LIVE -ErrorAction SilentlyContinue
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_PACKAGE_PATH -ErrorAction SilentlyContinue
    }

    if ($PSBoundParameters.ContainsKey('ModuleManifestPath') -and -not [string]::IsNullOrWhiteSpace($ModuleManifestPath)) {
        $env:PSPUBLISHMODULE_TEST_MANIFEST_PATH = (Resolve-Path -LiteralPath $ModuleManifestPath).Path
    } else {
        Remove-Item Env:\PSPUBLISHMODULE_TEST_MANIFEST_PATH -ErrorAction SilentlyContinue
    }

    if ($PSBoundParameters.ContainsKey('EvidenceFile') -and -not [string]::IsNullOrWhiteSpace($EvidenceFile)) {
        $evidenceDataPath = Join-Path ([IO.Path]::GetTempPath()) ("PSPublishModule.PrivateGallery.LiveEvidence." + [guid]::NewGuid().ToString('N') + ".json")
        $env:PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH = $evidenceDataPath
    } else {
        Remove-Item Env:\PSPUBLISHMODULE_AZDO_EVIDENCE_DATA_PATH -ErrorAction SilentlyContinue
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
        $validationItems = @()
        $evidenceValidationErrors = @()
        if (-not [string]::IsNullOrWhiteSpace($evidenceDataPath) -and (Test-Path -LiteralPath $evidenceDataPath -PathType Leaf)) {
            $rawEvidenceItems = Get-Content -LiteralPath $evidenceDataPath -Raw
            if (-not [string]::IsNullOrWhiteSpace($rawEvidenceItems)) {
                $parsedEvidenceItems = $rawEvidenceItems | ConvertFrom-Json
                $validationItems = @($parsedEvidenceItems)
            }
        }

        $pesterSucceeded = $failedCount -eq 0 -and $resultText -ne 'Failed'
        if ($pesterSucceeded) {
            $onboardingEvidence = $validationItems | Where-Object { $_.Name -eq 'OnboardingInstallUpdate' } | Select-Object -First 1
            if ($null -eq $onboardingEvidence) {
                $evidenceValidationErrors += "Required validation item 'OnboardingInstallUpdate' was not written."
            } else {
                if (-not (Test-ValidationItemSucceeded -Item $onboardingEvidence -PropertyName 'Succeeded')) {
                    $evidenceValidationErrors += "Validation item 'OnboardingInstallUpdate' did not report Succeeded = true."
                }
                if (-not (Test-ValidationItemSucceeded -Item $onboardingEvidence -PropertyName 'AccessProbeSucceeded')) {
                    $evidenceValidationErrors += "Validation item 'OnboardingInstallUpdate' did not prove AccessProbeSucceeded = true."
                }
                if ((Get-ValidationItemValue -Item $onboardingEvidence -PropertyName 'PublishConfigurationHasCredential') -ne $false) {
                    $evidenceValidationErrors += "Validation item 'OnboardingInstallUpdate' did not prove publish configuration was credential-free."
                }
                if (-not (Test-ValidationItemSucceeded -Item $onboardingEvidence -PropertyName 'InstallResultReturned')) {
                    $evidenceValidationErrors += "Validation item 'OnboardingInstallUpdate' did not prove install execution returned a result."
                }
                if (-not (Test-ValidationItemSucceeded -Item $onboardingEvidence -PropertyName 'UpdateResultReturned')) {
                    $evidenceValidationErrors += "Validation item 'OnboardingInstallUpdate' did not prove update execution returned a result."
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($resolvedPublishPackagePath)) {
                $publishEvidence = $validationItems | Where-Object { $_.Name -eq 'PublishPackage' } | Select-Object -First 1
                if ($null -eq $publishEvidence) {
                    $evidenceValidationErrors += "Required validation item 'PublishPackage' was not written."
                } else {
                    if (-not (Test-ValidationItemSucceeded -Item $publishEvidence -PropertyName 'Succeeded')) {
                        $evidenceValidationErrors += "Validation item 'PublishPackage' did not report Succeeded = true."
                    }
                    if (-not (Test-ValidationItemSucceeded -Item $publishEvidence -PropertyName 'AccessProbeSucceeded')) {
                        $evidenceValidationErrors += "Validation item 'PublishPackage' did not prove AccessProbeSucceeded = true."
                    }
                    if ([int] (Get-ValidationItemValue -Item $publishEvidence -PropertyName 'FailedCount') -ne 0) {
                        $evidenceValidationErrors += "Validation item 'PublishPackage' did not prove FailedCount = 0."
                    }

                    $pushedPackages = @(
                        @(Get-ValidationItemValue -Item $publishEvidence -PropertyName 'PushedPackageNames') |
                            Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
                    )
                    if ($pushedPackages.Count -eq 0) {
                        $evidenceValidationErrors += "Validation item 'PublishPackage' did not record pushed package names."
                    }
                }
            }
        }

        $evidenceDirectory = Split-Path -Path $EvidenceFile -Parent
        if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory) -and -not (Test-Path -LiteralPath $evidenceDirectory)) {
            New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
        }

        $evidence = [ordered]@{
            SchemaVersion        = 1
            GeneratedAtUtc       = [DateTimeOffset]::UtcNow.ToString('o')
            Succeeded            = $pesterSucceeded -and $evidenceValidationErrors.Count -eq 0
            Provider             = 'AzureArtifacts'
            Organization         = $Organization
            Project              = if ($PSBoundParameters.ContainsKey('Project') -and -not [string]::IsNullOrWhiteSpace($Project)) { $Project } else { $null }
            Feed                 = $Feed
            ModuleName           = $ModuleName
            ProfileName          = $ProfileName
            PublishPackageSupplied = -not [string]::IsNullOrWhiteSpace($resolvedPublishPackagePath)
            PublishPackageName   = if (-not [string]::IsNullOrWhiteSpace($resolvedPublishPackagePath)) { [IO.Path]::GetFileName($resolvedPublishPackagePath) } else { $null }
            GeneratedDisposablePackage = $GenerateDisposablePackage.IsPresent
            DisposablePackageName = if ($GenerateDisposablePackage.IsPresent) { $DisposablePackageName } else { $null }
            DisposablePackageVersion = if ($GenerateDisposablePackage.IsPresent) { $resolvedDisposablePackageVersion } else { $null }
            UnattendedCredentialProviderEnvironment = [ordered]@{
                ArtifactsExternalFeedEndpointsConfigured = -not [string]::IsNullOrWhiteSpace($env:ARTIFACTS_CREDENTIALPROVIDER_EXTERNAL_FEED_ENDPOINTS)
                ArtifactsFeedEndpointsConfigured = -not [string]::IsNullOrWhiteSpace($env:ARTIFACTS_CREDENTIALPROVIDER_FEED_ENDPOINTS)
                LegacyVssExternalFeedEndpointsConfigured = -not [string]::IsNullOrWhiteSpace($env:VSS_NUGET_EXTERNAL_FEED_ENDPOINTS)
            }
            ValidationItems      = $validationItems
            EvidenceValidationErrors = $evidenceValidationErrors
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

        if ($evidenceValidationErrors.Count -gt 0) {
            throw "Live Azure Artifacts evidence validation failed: $($evidenceValidationErrors -join ' ')"
        }
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

    if (-not [string]::IsNullOrWhiteSpace($evidenceDataPath) -and (Test-Path -LiteralPath $evidenceDataPath -PathType Leaf)) {
        Remove-Item -LiteralPath $evidenceDataPath -Force -ErrorAction SilentlyContinue
    }

    if (-not [string]::IsNullOrWhiteSpace($generatedPackageRoot) -and (Test-Path -LiteralPath $generatedPackageRoot)) {
        Remove-Item -LiteralPath $generatedPackageRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
