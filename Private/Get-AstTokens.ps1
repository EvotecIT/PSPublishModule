function Get-AstTokens {
    [cmdletBinding()]
    param(
        [System.Management.Automation.Language.Token[]] $ASTTokens,
        [System.Collections.Generic.List[Object]] $Commands
    )
    foreach ($_ in $astTokens) {
        if ($_.TokenFlags -eq 'Command' -and $_.Kind -eq 'Generic') {
            if ($_.Value -notin $Commands) {
                $Commands.Add($_)
            }
        } else {
            if ($_.NestedTokens) {
                Get-AstTokens -ASTTokens $_.NestedTokens -Commands $Commands
            }
        }
    }
}