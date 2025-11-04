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
                    if ($node.GetCommandName() -in 'Get-ADComputer', 'Get-ADUser', 'Get-ADObject', 'Get-ADDomainController', 'Get-ADReplicationSubnet') {
                        return $false
                    }
                }

                return $true
            }
        }, $true
    )

    # Language keywords and control tokens that can appear in CommandAsts under some constructs
    $Reserved = @(
        'if','elseif','else','switch','for','foreach','while','do','until',
        'try','catch','finally','throw','trap','break','continue','return',
        'function','filter','workflow','configuration','class','enum','data',
        'param','begin','process','end','in','using'
    )

    $List = foreach ($Function in $ListOfFuncionsAst) {
        try {
            $name = $Function.GetCommandName()
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if ($name -in $Reserved) { continue }
            # Exclude common redirection-like tokens that could surface as commands in edge cases
            if ($name -in '>', '>>', '2>', '2>>', '|') { continue }
            $name
        } catch {
            # Fallback to first element value when safe
            $Line = $Function.CommandElements[0]
            if ($Line -and $Line.Value -and ($Line.Value -notin $Reserved)) { $Line.Value }
        }
    }
    $List
}
