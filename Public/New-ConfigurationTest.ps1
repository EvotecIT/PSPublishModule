function New-ConfigurationTest {
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