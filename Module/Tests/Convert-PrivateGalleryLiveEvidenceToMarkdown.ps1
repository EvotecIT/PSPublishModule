<#
.SYNOPSIS
Formats private gallery live validation evidence as GitHub-flavored Markdown.

.DESCRIPTION
Converts the non-secret JSON written by Invoke-PrivateGalleryAzureArtifactsLiveValidation.ps1
into a concise Markdown summary for GitHub Actions step summaries and operator
handoff notes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $EvidenceFile
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function ConvertTo-MarkdownTableValue {
    param(
        [AllowNull()]
        [object] $Value
    )

    if ($null -eq $Value) {
        return ''
    }

    $text = if ($Value -is [array]) {
        ($Value | ForEach-Object { [string] $_ }) -join ', '
    } else {
        [string] $Value
    }

    return $text.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('### Private Gallery Live Validation')

if (-not (Test-Path -LiteralPath $EvidenceFile -PathType Leaf)) {
    $lines.Add('')
    $lines.Add('No evidence JSON was written. Check the validation step log.')
    $lines -join [Environment]::NewLine
    return
}

$evidence = Get-Content -LiteralPath $EvidenceFile -Raw | ConvertFrom-Json
$pester = $evidence.PSObject.Properties['Pester'].Value

$lines.Add('')
$lines.Add('| Field | Value |')
$lines.Add('| --- | --- |')
$lines.Add("| Succeeded | $(ConvertTo-MarkdownTableValue $evidence.Succeeded) |")
$lines.Add("| Organization | $(ConvertTo-MarkdownTableValue $evidence.Organization) |")
$lines.Add("| Project | $(ConvertTo-MarkdownTableValue $evidence.Project) |")
$lines.Add("| Feed | $(ConvertTo-MarkdownTableValue $evidence.Feed) |")
$lines.Add("| Module | $(ConvertTo-MarkdownTableValue $evidence.ModuleName) |")
$lines.Add("| Profile | $(ConvertTo-MarkdownTableValue $evidence.ProfileName) |")
$lines.Add("| Pester result | $(ConvertTo-MarkdownTableValue $pester.Result) |")
$lines.Add("| Passed / Failed / Skipped | $(ConvertTo-MarkdownTableValue $pester.PassedCount) / $(ConvertTo-MarkdownTableValue $pester.FailedCount) / $(ConvertTo-MarkdownTableValue $pester.SkippedCount) |")
$lines.Add("| Publish proof enabled | $(ConvertTo-MarkdownTableValue $evidence.PublishPackageSupplied) |")
$lines.Add("| Generated disposable package | $(ConvertTo-MarkdownTableValue $evidence.GeneratedDisposablePackage) |")

$unattendedEnvironment = $evidence.PSObject.Properties['UnattendedCredentialProviderEnvironment']
if ($unattendedEnvironment -and $null -ne $unattendedEnvironment.Value) {
    $providerEnvironment = $unattendedEnvironment.Value
    $lines.Add("| Credential-provider external endpoints configured | $(ConvertTo-MarkdownTableValue $providerEnvironment.ArtifactsExternalFeedEndpointsConfigured) |")
    $lines.Add("| Credential-provider feed endpoints configured | $(ConvertTo-MarkdownTableValue $providerEnvironment.ArtifactsFeedEndpointsConfigured) |")
    $lines.Add("| Legacy VSS external endpoints configured | $(ConvertTo-MarkdownTableValue $providerEnvironment.LegacyVssExternalFeedEndpointsConfigured) |")
}

$errors = @($evidence.EvidenceValidationErrors | Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) })
if ($errors.Count -gt 0) {
    $lines.Add('')
    $lines.Add('| Evidence validation error |')
    $lines.Add('| --- |')
    foreach ($errorItem in $errors) {
        $lines.Add("| $(ConvertTo-MarkdownTableValue $errorItem) |")
    }
}

$items = @($evidence.ValidationItems)
if ($items.Count -gt 0) {
    $lines.Add('')
    $lines.Add('| Validation item | Succeeded | Details |')
    $lines.Add('| --- | --- | --- |')
    foreach ($item in $items) {
        $details = @()
        if ($null -ne $item.PSObject.Properties['AccessProbeSucceeded'] -and $null -ne $item.AccessProbeSucceeded) {
            $details += "AccessProbe=$($item.AccessProbeSucceeded)"
        }
        if ($null -ne $item.PSObject.Properties['BootstrapPackageGenerated'] -and $null -ne $item.BootstrapPackageGenerated) {
            $details += "BootstrapPackage=$($item.BootstrapPackageGenerated)"
        }
        if ($null -ne $item.PSObject.Properties['BootstrapScriptExecuted'] -and $null -ne $item.BootstrapScriptExecuted) {
            $details += "BootstrapScript=$($item.BootstrapScriptExecuted)"
        }
        if ($null -ne $item.PSObject.Properties['CredentialProviderSessionPrimeAttempted'] -and [bool] $item.CredentialProviderSessionPrimeAttempted) {
            $details += "SessionPrime=$($item.CredentialProviderSessionPrimeSucceeded)"
        } elseif ($null -ne $item.PSObject.Properties['CredentialProviderSessionPrimeSkipped'] -and [bool] $item.CredentialProviderSessionPrimeSkipped) {
            $details += "SessionPrime=Skipped"
        }
        if ($null -ne $item.PSObject.Properties['InstallResultReturned'] -and $null -ne $item.InstallResultReturned) {
            $details += "Install=$($item.InstallResultReturned)"
        }
        if ($null -ne $item.PSObject.Properties['UpdateResultReturned'] -and $null -ne $item.UpdateResultReturned) {
            $details += "Update=$($item.UpdateResultReturned)"
        }
        if ($null -ne $item.PSObject.Properties['PushedPackageNames'] -and $null -ne $item.PushedPackageNames) {
            $details += "PushedPackages=$(@($item.PushedPackageNames).Count)"
        }
        if ($null -ne $item.PSObject.Properties['FailedCount'] -and $null -ne $item.FailedCount) {
            $details += "FailedPackages=$($item.FailedCount)"
        }

        $lines.Add("| $(ConvertTo-MarkdownTableValue $item.Name) | $(ConvertTo-MarkdownTableValue $item.Succeeded) | $(ConvertTo-MarkdownTableValue ($details -join ', ')) |")
    }
}

$lines -join [Environment]::NewLine
