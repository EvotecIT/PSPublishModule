# Source development binary loader
$PowerForgeDevelopmentBinaryRoot = {{BinaryRootExpression}}
$PowerForgeDevelopmentBinaryMode = '{{DevelopmentBinaryMode}}'
$PowerForgeDevelopmentBinaryEnvironmentVariable = '{{DevelopmentBinaryEnvironmentVariable}}'
$PowerForgeDevelopmentConfigurationEnvironmentVariable = '{{DevelopmentConfigurationEnvironmentVariable}}'
$PowerForgeDevelopmentCoreFrameworks = {{DevelopmentCoreFrameworks}}
$PowerForgeDevelopmentDesktopFrameworks = {{DevelopmentDesktopFrameworks}}
$PowerForgeDevelopmentUseAssemblyLoadContext = {{UseAssemblyLoadContext}}
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
    $PowerForgeDevelopmentConfigurations = @($PowerForgeDevelopmentConfigurations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

    $PowerForgeDevelopmentFrameworks = if ($PSEdition -eq 'Core') { $PowerForgeDevelopmentCoreFrameworks } else { $PowerForgeDevelopmentDesktopFrameworks }
    $PowerForgeDevelopmentBinaryPath = $null
    foreach ($PowerForgeDevelopmentConfiguration in $PowerForgeDevelopmentConfigurations) {
        foreach ($PowerForgeDevelopmentFramework in $PowerForgeDevelopmentFrameworks) {
            $PowerForgeDevelopmentCandidate = [IO.Path]::Combine($PowerForgeDevelopmentBinaryRoot, $PowerForgeDevelopmentConfiguration, $PowerForgeDevelopmentFramework, '{{LibraryFileName}}')
            if (Test-Path -LiteralPath $PowerForgeDevelopmentCandidate) {
                $PowerForgeDevelopmentBinaryPath = $PowerForgeDevelopmentCandidate
                break
            }
        }
        if ($PowerForgeDevelopmentBinaryPath) { break }
    }

    if ($PowerForgeDevelopmentBinaryPath) {
        try {
            $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
{{RuntimeHandlerBlock}}
            if ($PSEdition -eq 'Core' -and $PowerForgeDevelopmentUseAssemblyLoadContext) {
{{AssemblyLoadContextImportBlock}}
            } else {
                $PowerForgeDevelopmentLoadedType = '{{LibraryTypeName}}' -as [type]
                if ($PowerForgeDevelopmentLoadedType -and $PowerForgeDevelopmentLoadedType.Assembly) {
                    & $ImportModule -Assembly $PowerForgeDevelopmentLoadedType.Assembly -Force -ErrorAction Stop
                } else {
                    & $ImportModule $PowerForgeDevelopmentBinaryPath -ErrorAction Stop
                }
            }
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
