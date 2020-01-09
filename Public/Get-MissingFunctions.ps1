function Get-MissingFunctions {
    [CmdletBinding()]
    param(
        [alias('Path')][string] $FilePath,
        [string[]] $Functions,
        [switch] $Summary,
        [switch] $SummaryWithCommands,
        [Array] $ApprovedModules
    )
    $ListCommands = [System.Collections.Generic.List[Object]]::new()
    $Result = Get-ScriptCommands -FilePath $FilePath -CommandsOnly
    #$FilteredCommands = Get-FilteredScriptCommands -Commands $Result -NotUnknown -NotCmdlet -Functions $Functions -NotApplication -FilePath $FilePath
    $FilteredCommands = Get-FilteredScriptCommands -Commands $Result -NotCmdlet -Functions $Functions -NotApplication -FilePath $FilePath
    foreach ($_ in $FilteredCommands) {
        $ListCommands.Add($_)
    }
    # this gets commands along their ScriptBlock
    Get-RecursiveCommands -Commands $FilteredCommands

    $FunctionsOutput = foreach ($_ in $ListCommands) {
        if ($_.ScriptBlock) {
            if ($ApprovedModules.Count -gt 0 -and $_.Source -in $ApprovedModules) {
                "function $($_.Name) { $($_.ScriptBlock) }"
            } elseif ($ApprovedModules.Count -eq 0) {
                "function $($_.Name) { $($_.ScriptBlock) }"
            }
        }
    }
    if ($SummaryWithCommands) {
        $Hash = @{
            Summary         = $FilteredCommands
            SummaryFiltered = $ListCommands
            Functions       = $FunctionsOutput
        }
        return $Hash
    } elseif ($Summary) {
        return $ListCommands
    } else {
        return $FunctionsOutput
    }
}