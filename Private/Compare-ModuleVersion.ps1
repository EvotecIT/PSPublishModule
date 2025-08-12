function Compare-ModuleVersion {
    <#
    .SYNOPSIS
    Compares installed module version against requirements

    .DESCRIPTION
    Helper function to compare module versions and determine if installation/update is needed.
    Supports RequiredVersion, MinimumVersion, and MaximumVersion constraints.

    .PARAMETER InstalledVersion
    The currently installed version of the module

    .PARAMETER RequiredVersion
    The exact version required (if specified)

    .PARAMETER MinimumVersion
    The minimum version required (if specified)

    .PARAMETER MaximumVersion
    The maximum version allowed (if specified)

    .EXAMPLE
    Compare-ModuleVersion -InstalledVersion "1.0.0" -RequiredVersion "1.0.1"

    .EXAMPLE
    Compare-ModuleVersion -InstalledVersion "1.0.0" -MinimumVersion "1.0.1"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$InstalledVersion,

        [Parameter()]
        [string]$RequiredVersion,

        [Parameter()]
        [string]$MinimumVersion,

        [Parameter()]
        [string]$MaximumVersion
    )

    try {
        $installed = [System.Version]::Parse($InstalledVersion)

        if ($RequiredVersion) {
            $required = [System.Version]::Parse($RequiredVersion)
            return @{
                NeedsInstall   = $installed -ne $required
                InstallVersion = $RequiredVersion
                Reason         = "Exact version required: $RequiredVersion (installed: $InstalledVersion)"
            }
        }

        $needsUpdate = $false
        $reason = ""

        if ($MinimumVersion) {
            $minimum = [System.Version]::Parse($MinimumVersion)
            if ($installed -lt $minimum) {
                $needsUpdate = $true
                $reason = "Below minimum version: $MinimumVersion (installed: $InstalledVersion)"
            }
        }

        if ($MaximumVersion) {
            $maximum = [System.Version]::Parse($MaximumVersion)
            if ($installed -gt $maximum) {
                # Don't downgrade unless it's a required version
                $reason = "Above maximum version: $MaximumVersion (installed: $InstalledVersion) - keeping newer"
            }
        }

        return @{
            NeedsInstall   = $needsUpdate
            InstallVersion = $MinimumVersion
            Reason         = $reason
        }
    } catch {
        Write-Error "Failed to compare module versions: $($_.Exception.Message)"
        throw
    }
}
