<#
.SYNOPSIS
Checks whether a GitHub repository is configured for PSPublishModule private gallery live validation.

.DESCRIPTION
This helper is an operator preflight for the opt-in Azure Artifacts live validation workflows.
It checks repository variables and Actions secrets by name only; it never prints secret values.
By default the helper fails when required workflow variables are missing. Use -NoFail when
you want a report-only inventory.

.EXAMPLE
.\Module\Tests\Test-PrivateGalleryGitHubLiveValidationConfiguration.ps1 -Repository EvotecIT/PSPublishModule

.EXAMPLE
.\Module\Tests\Test-PrivateGalleryGitHubLiveValidationConfiguration.ps1 `
    -Repository EvotecIT/PSPublishModule `
    -RequireUnattendedCredentialProviderSecret `
    -Markdown
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateNotNullOrEmpty()]
    [string] $Repository = 'EvotecIT/PSPublishModule',

    [Parameter()]
    [switch] $RequireUnattendedCredentialProviderSecret,

    [Parameter()]
    [switch] $Markdown,

    [Parameter()]
    [switch] $NoFail,

    [Parameter()]
    [switch] $PassThru,

    [Parameter(DontShow)]
    [string] $VariableJson,

    [Parameter(DontShow)]
    [string] $SecretJson
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$requiredVariables = @(
    'PSPUBLISHMODULE_AZDO_ORGANIZATION',
    'PSPUBLISHMODULE_AZDO_FEED',
    'PSPUBLISHMODULE_AZDO_MODULE_NAME'
)

$optionalVariables = @(
    'PSPUBLISHMODULE_AZDO_PROJECT',
    'PSPUBLISHMODULE_AZDO_PROFILE_NAME',
    'PSPUBLISHMODULE_AZDO_RUNNER_LABELS',
    'PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_NAME',
    'PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_VERSION'
)

$credentialProviderSecrets = @(
    'PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS',
    'PSPUBLISHMODULE_AZDO_ARTIFACTS_FEED_ENDPOINTS',
    'PSPUBLISHMODULE_AZDO_VSS_NUGET_EXTERNAL_FEED_ENDPOINTS'
)

function Invoke-GitHubJson {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed: $output"
    }

    return ($output | Out-String)
}

function ConvertTo-NameSet {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Json
    )

    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    if ([string]::IsNullOrWhiteSpace($Json)) {
        Write-Output -NoEnumerate $set
        return
    }

    $items = @((ConvertFrom-Json -InputObject $Json))
    foreach ($item in $items) {
        $nameProperty = $item.PSObject.Properties['name']
        if ($nameProperty -and -not [string]::IsNullOrWhiteSpace([string] $nameProperty.Value)) {
            [void] $set.Add([string] $nameProperty.Value)
        }
    }

    Write-Output -NoEnumerate $set
}

$variableJsonSupplied = $PSBoundParameters.ContainsKey('VariableJson')
$secretJsonSupplied = $PSBoundParameters.ContainsKey('SecretJson')

if (-not $variableJsonSupplied) {
    $VariableJson = Invoke-GitHubJson -Arguments @('variable', 'list', '--repo', $Repository, '--json', 'name,updatedAt')
}

if (-not $secretJsonSupplied) {
    $SecretJson = Invoke-GitHubJson -Arguments @('secret', 'list', '--repo', $Repository, '--app', 'actions', '--json', 'name,updatedAt')
}

$variableNames = ConvertTo-NameSet -Json $VariableJson
$secretNames = ConvertTo-NameSet -Json $SecretJson

$presentRequiredVariables = @($requiredVariables | Where-Object { $variableNames.Contains($_) })
$missingRequiredVariables = @($requiredVariables | Where-Object { -not $variableNames.Contains($_) })
$presentOptionalVariables = @($optionalVariables | Where-Object { $variableNames.Contains($_) })
$missingOptionalVariables = @($optionalVariables | Where-Object { -not $variableNames.Contains($_) })
$presentCredentialProviderSecrets = @($credentialProviderSecrets | Where-Object { $secretNames.Contains($_) })
$missingCredentialProviderSecrets = @($credentialProviderSecrets | Where-Object { -not $secretNames.Contains($_) })
$unattendedCredentialProviderSecretConfigured = $presentCredentialProviderSecrets.Count -gt 0

$requiredActions = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in $missingRequiredVariables) {
    [void] $requiredActions.Add("Define repository variable '$name'.")
}

if ($RequireUnattendedCredentialProviderSecret -and -not $unattendedCredentialProviderSecretConfigured) {
    [void] $requiredActions.Add("Define at least one supported Azure Artifacts Credential Provider Actions secret: $($credentialProviderSecrets -join ', ').")
}

$variablePlaceholders = @{
    PSPUBLISHMODULE_AZDO_ORGANIZATION                 = '<azure-devops-organization>'
    PSPUBLISHMODULE_AZDO_FEED                         = '<azure-artifacts-feed>'
    PSPUBLISHMODULE_AZDO_MODULE_NAME                  = '<known-module-name>'
    PSPUBLISHMODULE_AZDO_PROJECT                      = '<azure-devops-project>'
    PSPUBLISHMODULE_AZDO_PROFILE_NAME                 = '<profile-name>'
    PSPUBLISHMODULE_AZDO_RUNNER_LABELS                = '["self-hosted","windows"]'
    PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_NAME      = 'PSPublishModule.PrivateGallery.LiveValidation'
    PSPUBLISHMODULE_AZDO_DISPOSABLE_PACKAGE_VERSION   = '<optional-semver>'
}

$setupCommands = New-Object 'System.Collections.Generic.List[string]'
foreach ($name in @($missingRequiredVariables + $missingOptionalVariables)) {
    $placeholder = $variablePlaceholders[$name]
    if ([string]::IsNullOrWhiteSpace($placeholder)) {
        $placeholder = '<value>'
    }

    [void] $setupCommands.Add("gh variable set $name --repo $Repository --body '$placeholder'")
}

if ($RequireUnattendedCredentialProviderSecret -and -not $unattendedCredentialProviderSecretConfigured) {
    [void] $setupCommands.Add("gh secret set PSPUBLISHMODULE_AZDO_ARTIFACTS_EXTERNAL_FEED_ENDPOINTS --repo $Repository < external-feed-endpoints.json")
    [void] $setupCommands.Add("# Alternative supported secret names: PSPUBLISHMODULE_AZDO_ARTIFACTS_FEED_ENDPOINTS, PSPUBLISHMODULE_AZDO_VSS_NUGET_EXTERNAL_FEED_ENDPOINTS")
}

$dispatchCommands = @(
    "gh workflow run BuildModule.yml --repo $Repository --ref <feature-or-main-branch> -f privateGalleryLiveValidation=true -f privateGalleryGenerateDisposablePackage=true",
    "gh workflow run private-gallery-live-validation.yml --repo $Repository --ref main -f generateDisposablePackage=true"
)

$succeeded = $missingRequiredVariables.Count -eq 0 -and
    (-not $RequireUnattendedCredentialProviderSecret -or $unattendedCredentialProviderSecretConfigured)

$result = [pscustomobject]@{
    Repository                                   = $Repository
    Succeeded                                    = $succeeded
    RequiredVariablesPresent                     = $presentRequiredVariables
    RequiredVariablesMissing                     = $missingRequiredVariables
    OptionalVariablesPresent                     = $presentOptionalVariables
    OptionalVariablesMissing                     = $missingOptionalVariables
    CredentialProviderSecretsPresent             = $presentCredentialProviderSecrets
    CredentialProviderSecretsMissing             = $missingCredentialProviderSecrets
    UnattendedCredentialProviderSecretConfigured = $unattendedCredentialProviderSecretConfigured
    RequiredActions                              = @($requiredActions)
    SuggestedSetupCommands                       = @($setupCommands)
    SuggestedDispatchCommands                    = @($dispatchCommands)
}

if ($Markdown) {
    $status = if ($result.Succeeded) { 'Ready' } else { 'Not ready' }
    @(
        '### Private Gallery GitHub Configuration'
        ''
        '| Field | Value |'
        '| --- | --- |'
        "| Repository | $($result.Repository) |"
        "| Status | $status |"
        "| Required variables present | $($result.RequiredVariablesPresent.Count) / $($requiredVariables.Count) |"
        "| Optional variables present | $($result.OptionalVariablesPresent.Count) / $($optionalVariables.Count) |"
        "| Credential-provider secrets present | $($result.CredentialProviderSecretsPresent.Count) / $($credentialProviderSecrets.Count) |"
        "| Unattended credential-provider auth configured | $($result.UnattendedCredentialProviderSecretConfigured) |"
    )

    if ($result.RequiredActions.Count -gt 0) {
        ''
        'Required actions:'
        foreach ($action in $result.RequiredActions) {
            "- $action"
        }
    }

    if ($result.SuggestedSetupCommands.Count -gt 0) {
        ''
        'Suggested setup commands:'
        '```powershell'
        foreach ($command in $result.SuggestedSetupCommands) {
            $command
        }
        '```'
    }

    ''
    'Suggested dispatch commands:'
    '```powershell'
    foreach ($command in $result.SuggestedDispatchCommands) {
        $command
    }
    '```'
}

if ($PassThru -or -not $Markdown) {
    $result
}

if (-not $NoFail -and -not $result.Succeeded) {
    throw "Private gallery live validation is not ready for '$Repository'. Missing: $($result.RequiredActions -join ' ')"
}
