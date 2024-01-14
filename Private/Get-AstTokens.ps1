function Get-AstTokens {
    [cmdletBinding()]
    param(
        [System.Management.Automation.Language.Token[]] $ASTTokens,
        [System.Collections.Generic.List[Object]] $Commands,
        [System.Management.Automation.Language.Ast] $FileAst
    )

    $ListOfFuncionsAst = $FileAst.FindAll( {
            param([System.Management.Automation.Language.Ast] $ast)
            end {
                if ($ast -isnot [System.Management.Automation.Language.CommandAst]) {
                    return $false
                }

                for ($node = $ast.Parent; $null -ne $node; $node = $node.Parent) {
                    if ($node -isnot [System.Management.Automation.Language.CommandAst]) {
                        continue
                    }

                    # This is to prevent filters from AD based cmdlets from being included in the list
                    # Since sometimes scriptblock is used in the filter, instead of a string it causes filter variables to be included in the list
                    if ($node.GetCommandName() -in 'Get-ADComputer', 'Get-ADUser', 'Get-ADObject') {
                        return $false
                    }
                }

                return $true
            }
        }, $true
    )

    $List = foreach ($Function in $ListOfFuncionsAst) {
        $Line = $Function.CommandElements[0]
        if ($Line.Value) {
            $Line.Value
        }
    }
    $List
}