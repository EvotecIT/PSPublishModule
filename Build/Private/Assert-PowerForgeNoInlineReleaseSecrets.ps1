function Assert-PowerForgeNoInlineReleaseSecrets {
    <#
    .SYNOPSIS
    Rejects inline secret values before an effective release configuration is persisted or published.
    .PARAMETER Configuration
    Effective release configuration to inspect recursively.
    .NOTES
    Secret file paths and environment-variable names remain supported. Only direct values in canonical secret properties are rejected.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Configuration
    )

    $secretPropertyNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    @(
        'AccessKey'
        'AccessToken'
        'ApiKey'
        'ApiToken'
        'AssetToken'
        'CertificatePFXBase64'
        'CertificatePFXPassword'
        'ClientSecret'
        'DemoAccountPassword'
        'GitHubAccessToken'
        'GitHubToken'
        'NugetCredentialSecret'
        'Password'
        'PfxPassword'
        'PublishApiKey'
        'Secret'
        'Token'
    ) | ForEach-Object { [void] $secretPropertyNames.Add($_) }

    function Test-Node {
        param(
            [object] $Value,
            [string] $Path
        )

        if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) {
            return
        }
        if ($Value -is [Collections.IDictionary]) {
            foreach ($key in $Value.Keys) {
                Test-Property -Name ([string] $key) -Value $Value[$key] -Path $Path
            }
            return
        }
        if ($Value -is [Collections.IEnumerable]) {
            $index = 0
            foreach ($item in $Value) {
                Test-Node -Value $item -Path "$Path[$index]"
                $index++
            }
            return
        }
        foreach ($property in $Value.PSObject.Properties) {
            Test-Property -Name $property.Name -Value $property.Value -Path $Path
        }
    }

    function Test-Property {
        param(
            [string] $Name,
            [object] $Value,
            [string] $Path
        )

        $propertyPath = if ([string]::IsNullOrWhiteSpace($Path)) { $Name } else { "$Path.$Name" }
        if ($secretPropertyNames.Contains($Name) -and
            $Value -is [string] -and
            -not [string]::IsNullOrWhiteSpace([string] $Value)) {
            throw "Inline release secret '$propertyPath' cannot be persisted in authorized configuration evidence. Use its FilePath or EnvName setting."
        }
        Test-Node -Value $Value -Path $propertyPath
    }

    Test-Node -Value $Configuration -Path ''
}
