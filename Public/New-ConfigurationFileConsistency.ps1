function New-ConfigurationFileConsistency {
    <#
    .SYNOPSIS
    Creates configuration for file consistency checking (encoding and line endings) during module build.

    .DESCRIPTION
    Configures file encoding and line ending consistency analysis to be performed during the module build process.
    Can enforce consistency requirements and fail the build if issues are found.

    .PARAMETER Enable
    Enable file consistency checking during build.

    .PARAMETER FailOnInconsistency
    Fail the build if consistency issues are found.

    .PARAMETER RequiredEncoding
    Required file encoding. Build will fail if files don't match this encoding.
    Valid values: 'ASCII', 'UTF8', 'UTF8BOM', 'Unicode', etc.

    .PARAMETER RequiredLineEnding
    Required line ending style. Build will fail if files don't match this style.
    Valid values: 'CRLF', 'LF'

    .PARAMETER AutoFix
    Automatically fix encoding and line ending issues during build.

    .PARAMETER CreateBackups
    Create backup files before applying automatic fixes.

    .PARAMETER MaxInconsistencyPercentage
    Maximum percentage of files that can have consistency issues. Default is 5%.

    .PARAMETER ExcludeDirectories
    Directory names to exclude from consistency analysis.

    .PARAMETER ExportReport
    Export detailed consistency report to the artifacts directory.

    .PARAMETER ReportFileName
    Custom filename for the consistency report. Default is 'FileConsistencyReport.csv'.

    .PARAMETER CheckMixedLineEndings
    Check for files with mixed line endings (both CRLF and LF in same file).

    .PARAMETER CheckMissingFinalNewline
    Check for files missing final newlines.

    .EXAMPLE
    New-ConfigurationFileConsistency -Enable -RequiredEncoding UTF8BOM -RequiredLineEnding CRLF
    Enable consistency checking with specific encoding and line ending requirements.

    .EXAMPLE
    New-ConfigurationFileConsistency -Enable -AutoFix -CreateBackups
    Enable checking with automatic fixing and backup creation.

    .EXAMPLE
    New-ConfigurationFileConsistency -Enable -FailOnInconsistency -MaxInconsistencyPercentage 10
    Enable checking and fail build if more than 10% of files have issues.

    .NOTES
    This function is part of the PSPublishModule DSL for configuring module builds.
    Use within Build-Module script blocks to configure file consistency checking.
    #>
    [CmdletBinding()]
    param(
        [switch] $Enable,
        [switch] $FailOnInconsistency,
        [ValidateSet('ASCII', 'UTF8', 'UTF8BOM', 'Unicode', 'BigEndianUnicode', 'UTF7', 'UTF32')]
        [string] $RequiredEncoding = 'UTF8BOM',
        [ValidateSet('CRLF', 'LF')]
        [string] $RequiredLineEnding = 'CRLF',
        [switch] $AutoFix,
        [switch] $CreateBackups,
        [ValidateRange(0, 100)]
        [int] $MaxInconsistencyPercentage = 5,
        [string[]] $ExcludeDirectories = @('Artefacts', 'Ignore', '.git', '.vs', 'bin', 'obj'),
        [switch] $ExportReport,
        [string] $ReportFileName = 'FileConsistencyReport.csv',
        [switch] $CheckMixedLineEndings,
        [switch] $CheckMissingFinalNewline
    )

    $Configuration = [ordered] @{
        Type     = 'FileConsistency'
        Settings = [ordered] @{
            Enable                     = $Enable.IsPresent
            FailOnInconsistency        = $FailOnInconsistency.IsPresent
            RequiredEncoding           = $RequiredEncoding
            RequiredLineEnding         = $RequiredLineEnding
            AutoFix                    = $AutoFix.IsPresent
            CreateBackups              = $CreateBackups.IsPresent
            MaxInconsistencyPercentage = $MaxInconsistencyPercentage
            ExcludeDirectories         = $ExcludeDirectories
            ExportReport               = $ExportReport.IsPresent
            ReportFileName             = $ReportFileName
            CheckMixedLineEndings      = $CheckMixedLineEndings.IsPresent
            CheckMissingFinalNewline   = $CheckMissingFinalNewline.IsPresent
        }
    }

    return $Configuration
}