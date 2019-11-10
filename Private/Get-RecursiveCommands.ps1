function Get-RecursiveCommands {
    [CmdletBinding()]
    param(
        [Array] $Commands
    )
    $Another = foreach ($Command in $Commands) {
        if ($Command.ScriptBlock) {
            Get-ScriptCommands -Code $Command.ScriptBlock -CommandsOnly
        }
    }

    $filter = Get-FilteredScriptCommands -Commands $Another -NotUnknown -NotCmdlet
    [Array] $ProcessedCommands = foreach ($_ in $Filter) {
        if ($_.Name -notin $ListCommands.Name) {
            $ListCommands.Add($_)
            $_
        }
    }
    if ($ProcessedCommands.Count -gt 0) {
        Get-RecursiveCommands -Commands $ProcessedCommands
    }
}