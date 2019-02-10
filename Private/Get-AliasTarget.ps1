#using Namespace System.Management.Automation.Language

Function Get-AliasTarget {
    [cmdletbinding()]
    param (
        [Parameter(Mandatory, ValueFromPipeline, ValueFromPipelineByPropertyName)]
        [Alias('PSPath', 'FullName')]
        [string[]]$Path
    )

    process {
        foreach ($File in $Path) {
            $FileAst = [System.Management.Automation.Language.Parser]::ParseFile($File, [ref]$null, [ref]$null)

            $FunctionName = $FileAst.FindAll( {
                    param ($ast)

                    $ast -is [System.Management.Automation.Language.FunctionDefinitionAst]
                }, $true).Name

            $AliasDefinitions = $FileAst.FindAll( {
                    param ($ast)

                    $ast -is [System.Management.Automation.Language.StringConstantExpressionAst] -And $ast.Value -match '(New|Set)-Alias'
                }, $true)

            $AliasTarget = $AliasDefinitions.Parent.CommandElements.Where( {
                    $_.StringConstantType -eq 'BareWord' -and
                    $_.Value -notin ('New-Alias', 'Set-Alias', $FunctionName)
                }).Value

            $Attributes = $FileAst.FindAll( {
                    param ($ast)

                    $ast -is [System.Management.Automation.Language.AttributeAst]
                }, $true)

            $AliasDefinitions = $Attributes.Where( {$_.TypeName.Name -eq 'Alias' -and $_.Parent -is [System.Management.Automation.Language.ParamBlockAst]})

            $AliasTarget += $AliasDefinitions.PositionalArguments.Value

            [PsCustomObject]@{
                Function = $FunctionName
                Alias    = $AliasTarget
            }
        }
    }
}

#Get-AliasTarget -Path 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1' | Select-Object -ExpandProperty Alias


#Get-FunctionAliases -Path 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1'