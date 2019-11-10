function Get-MissingFunctions {
    [CmdletBinding()]
    param(
        [alias('Path')][string] $FilePath,
        [string[]] $Functions,
        [switch] $Summary
    )
    $ListCommands = [System.Collections.Generic.List[Object]]::new()
    $Result = Get-ScriptCommands -FilePath $FilePath -CommandsOnly
    $FilteredCommands = Get-FilteredScriptCommands -Commands $Result -NotUnknown -NotCmdlet -Functions $Functions
    $FilteredCommands = $FilteredCommands | Where-Object { $_.Source -ne 'PSWriteHTML' }

    foreach ($_ in $FilteredCommands) {
        $ListCommands.Add($_)
    }

    # this gets commands along their ScriptBlock
    Get-RecursiveCommands -Commands $FilteredCommands

    if ($Summary) {
        return $ListCommands
    } else {
        $Output = foreach ($_ in $ListCommands) {
            "function $($_.Name) { $($_.ScriptBlock) }"
        }
        $Output
    }
}
