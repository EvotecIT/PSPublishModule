function New-ConfigurationCompatibility {
    <#
    .SYNOPSIS
    Creates configuration for PowerShell compatibility checking during module build.

    .DESCRIPTION
    Configures PowerShell version compatibility analysis to be performed during the module build process.
    Can enforce compatibility requirements and fail the build if compatibility issues are found.

    .PARAMETER Enable
    Enable PowerShell compatibility checking during build.

    .PARAMETER FailOnIncompatibility
    Fail the build if compatibility issues are found.

    .PARAMETER RequirePS51Compatibility
    Require PowerShell 5.1 compatibility. Build will fail if any files are incompatible.

    .PARAMETER RequirePS7Compatibility
    Require PowerShell 7 compatibility. Build will fail if any files are incompatible.

    .PARAMETER RequireCrossCompatibility
    Require cross-version compatibility (both PS 5.1 and PS 7). Build will fail if any files are incompatible.

    .PARAMETER MinimumCompatibilityPercentage
    Minimum percentage of files that must be cross-compatible. Default is 95%.

    .PARAMETER ExcludeDirectories
    Directory names to exclude from compatibility analysis.

    .PARAMETER ExportReport
    Export detailed compatibility report to the artifacts directory.

    .PARAMETER ReportFileName
    Custom filename for the compatibility report. Default is 'PowerShellCompatibilityReport.csv'.

    .EXAMPLE
    New-ConfigurationCompatibility -Enable -RequireCrossCompatibility
    Enable compatibility checking and require all files to be cross-compatible.

    .EXAMPLE
    New-ConfigurationCompatibility -Enable -MinimumCompatibilityPercentage 90 -ExportReport
    Enable checking with 90% minimum compatibility and export detailed report.

    .EXAMPLE
    New-ConfigurationCompatibility -Enable -RequirePS51Compatibility -FailOnIncompatibility
    Require PS 5.1 compatibility and fail build if issues are found.

    .NOTES
    This function is part of the PSPublishModule DSL for configuring module builds.
    Use within Build-Module script blocks to configure compatibility checking.
    #>
    [CmdletBinding()]
    param(
        [switch] $Enable,
        [switch] $FailOnIncompatibility,
        [switch] $RequirePS51Compatibility,
        [switch] $RequirePS7Compatibility,
        [switch] $RequireCrossCompatibility,
        [ValidateRange(0, 100)]
        [int] $MinimumCompatibilityPercentage = 95,
        [string[]] $ExcludeDirectories = @('Artefacts', 'Ignore', '.git', '.vs', 'bin', 'obj'),
        [switch] $ExportReport,
        [string] $ReportFileName = 'PowerShellCompatibilityReport.csv'
    )

    $Configuration = [ordered] @{
        Type = 'Compatibility'
        Settings = [ordered] @{
            Enable = $Enable.IsPresent
            FailOnIncompatibility = $FailOnIncompatibility.IsPresent
            RequirePS51Compatibility = $RequirePS51Compatibility.IsPresent
            RequirePS7Compatibility = $RequirePS7Compatibility.IsPresent
            RequireCrossCompatibility = $RequireCrossCompatibility.IsPresent
            MinimumCompatibilityPercentage = $MinimumCompatibilityPercentage
            ExcludeDirectories = $ExcludeDirectories
            ExportReport = $ExportReport.IsPresent
            ReportFileName = $ReportFileName
        }
    }

    return $Configuration
}