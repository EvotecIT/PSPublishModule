function New-ConfigurationTest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('BeforeMerge', 'AfterMerge')][string[]] $When,
        [Parameter(Mandatory)][string] $TestsPath,
        [switch] $Enable,
        [switch] $Force
    )

    if ($Enable) {
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