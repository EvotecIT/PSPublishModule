function New-ConfigurationTest {
    <#
    .SYNOPSIS
    Configures running Pester tests as part of the build.

    .DESCRIPTION
    Emits test configuration that the builder uses to run tests. Currently, tests
    are triggered AfterMerge. When -Enable is not provided, nothing is emitted.

    .PARAMETER TestsPath
    Path to the folder containing Pester tests.

    .PARAMETER Enable
    Enable test execution in the build.

    .PARAMETER Force
    Force running tests even if they already ran or when caching would skip them.

    .EXAMPLE
    New-ConfigurationTest -Enable -TestsPath 'Tests' -Force
    Configures tests to run after merge from the 'Tests' folder.
    #>
    [CmdletBinding()]
    param(
        #[Parameter(Mandatory)][ValidateSet('BeforeMerge', 'AfterMerge')][string[]] $When,
        [Parameter(Mandatory)][string] $TestsPath,
        [switch] $Enable,
        [switch] $Force
    )

    if ($Enable) {
        if ($null -eq $IsWindows -or $IsWindows -eq $true) {
            $TestsPath = $TestsPath.Replace('/', '\')
        } else {
            $TestsPath = $TestsPath.Replace('\', '/')
        }
        # lets temporary set it here only, not sure if it's worth before merge
        $When = 'AfterMerge'
        foreach ($W in $When) {
            $Configuration = [ordered] @{
                Type          = "Tests$W"
                Configuration = [ordered] @{
                    When      = $W
                    TestsPath = $TestsPath
                    Force     = $Force.ispresent
                }
            }
            Remove-EmptyValue -Hashtable $Configuration.Configuration
            $Configuration
        }
    }
}
