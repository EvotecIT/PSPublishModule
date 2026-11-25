# Source development binary loader
$PowerForgeDevelopmentBinaryRoot = {{BinaryRootExpression}}
$PowerForgeDevelopmentBinaryMode = '{{DevelopmentBinaryMode}}'
$PowerForgeDevelopmentBinaryEnvironmentVariable = '{{DevelopmentBinaryEnvironmentVariable}}'
$PowerForgeDevelopmentConfigurationEnvironmentVariable = '{{DevelopmentConfigurationEnvironmentVariable}}'
$PowerForgeDevelopmentCoreFrameworks = {{DevelopmentCoreFrameworks}}
$PowerForgeDevelopmentDesktopFrameworks = {{DevelopmentDesktopFrameworks}}
$PowerForgeDevelopmentEnabled = $false
if ($PowerForgeDevelopmentBinaryMode -eq 'Auto') {
    $PowerForgeDevelopmentEnabled = $true
} elseif ($PowerForgeDevelopmentBinaryMode -eq 'Environment') {
    $PowerForgeDevelopmentRequestedValue = [Environment]::GetEnvironmentVariable($PowerForgeDevelopmentBinaryEnvironmentVariable)
    $PowerForgeDevelopmentEnabled = [string]::Equals($PowerForgeDevelopmentRequestedValue, 'true', [StringComparison]::OrdinalIgnoreCase)
}

if ($PowerForgeDevelopmentEnabled) {
    $PowerForgeDevelopmentConfigurations = @()
    $PowerForgeDevelopmentRequestedConfiguration = [Environment]::GetEnvironmentVariable($PowerForgeDevelopmentConfigurationEnvironmentVariable)
    if (-not [string]::IsNullOrWhiteSpace($PowerForgeDevelopmentRequestedConfiguration)) {
        $PowerForgeDevelopmentConfigurations += $PowerForgeDevelopmentRequestedConfiguration
    }
    $PowerForgeDevelopmentConfigurations += 'Debug'
    $PowerForgeDevelopmentConfigurations += 'Release'
    $PowerForgeDevelopmentConfigurationSet = [System.Collections.Generic.HashSet[string]]::new()
    $PowerForgeDevelopmentConfigurations = @(foreach ($PowerForgeDevelopmentConfiguration in $PowerForgeDevelopmentConfigurations) {
        if (-not [string]::IsNullOrWhiteSpace($PowerForgeDevelopmentConfiguration) -and $PowerForgeDevelopmentConfigurationSet.Add($PowerForgeDevelopmentConfiguration)) {
            $PowerForgeDevelopmentConfiguration
        }
    })

    $PowerForgeDevelopmentFrameworks = if ($PSEdition -eq 'Core') { $PowerForgeDevelopmentCoreFrameworks } else { $PowerForgeDevelopmentDesktopFrameworks }
    $PowerForgeDevelopmentBinaryPath = $null
    foreach ($PowerForgeDevelopmentConfiguration in $PowerForgeDevelopmentConfigurations) {
        foreach ($PowerForgeDevelopmentFramework in $PowerForgeDevelopmentFrameworks) {
            $PowerForgeDevelopmentCandidate = [IO.Path]::Combine($PowerForgeDevelopmentBinaryRoot, $PowerForgeDevelopmentConfiguration, $PowerForgeDevelopmentFramework, '{{LibraryFileName}}')
            if ([IO.File]::Exists($PowerForgeDevelopmentCandidate)) {
                $PowerForgeDevelopmentBinaryPath = $PowerForgeDevelopmentCandidate
                break
            }
        }
        if ($PowerForgeDevelopmentBinaryPath) { break }
    }

    if ($PowerForgeDevelopmentBinaryPath) {
        try {
{{ModuleLoaderImportBlock}}
{{SharedAssembliesBlock}}
            $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
{{RuntimeHandlerBlock}}
{{ImportBlock}}
{{DesktopTypeAcceleratorBlock}}
            $PowerForgeDevelopmentBinaryLoaded = $true
        } catch {
            if ($ErrorActionPreference -eq 'Stop') {
                throw
            } else {
                Write-Warning -Message "Importing development binary $PowerForgeDevelopmentBinaryPath failed. Falling back to packaged loader when available. Error: $($_.Exception.Message)"
            }
        }
    }
}
