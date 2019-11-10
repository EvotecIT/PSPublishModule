Function Get-AliasTarget {
    [cmdletbinding()]
    param (

        [Alias('PSPath', 'FullName')][Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)][string[]]$Path,
        [string] $Content,
        [switch] $RecurseFunctionNames
    )

    process {
        if ($Content) {
            $ProcessData = $Content
            $Code = $true
        } else {
            $ProcessData = $Path
            $Code = $false
        }
        foreach ($File in $ProcessData) {
            $Ast = $null
            if ($Code) {
                $FileAst = [System.Management.Automation.Language.Parser]::ParseInput($File, [ref]$null, [ref]$null)
            } else {
                $FileAst = [System.Management.Automation.Language.Parser]::ParseFile($File , [ref]$null, [ref]$null)
            }
            $FunctionName = $FileAst.FindAll( {
                    param ($ast)
                    $ast -is [System.Management.Automation.Language.FunctionDefinitionAst]
                }, $RecurseFunctionNames).Name


            $Ast = $Null
            $AliasDefinitions = $FileAst.FindAll( {
                    param ( $ast )
                    $ast -is [System.Management.Automation.Language.AttributeAst] -and
                    $ast.TypeName.Name -eq 'Alias' -and
                    $ast.Parent -is [System.Management.Automation.Language.ParamBlockAst]
                }, $true)

            $AliasTarget = @(
                $AliasDefinitions.PositionalArguments.Value
                foreach ($_ in  $AliasDefinitions.Parent.CommandElements) {
                    if ($_.StringConstantType -eq 'BareWord' -and $_.Value -notin ('New-Alias', 'Set-Alias', $FunctionName)) {
                        $_.Value
                    }
                }
            )

            <#
            [Array] $AliasTarget = @(
                if ($AliasDefinitions) {
                    $AliasDefinitions.Parent.CommandElements.Where( {
                            $_.StringConstantType -eq 'BareWord' -and
                            $_.Value -notin ('New-Alias', 'Set-Alias', $FunctionName)
                        }).Value
                }


                $Attributes = $FileAst.FindAll( {
                        param ($ast)

                        $ast -is [System.Management.Automation.Language.AttributeAst]
                    }, $true)
                $AliasDefinitions = $Attributes.Where( { $_.TypeName.Name -eq 'Alias' -and $_.Parent -is [System.Management.Automation.Language.ParamBlockAst] })
                $AliasDefinitions.PositionalArguments.Value
            )
            #>

            $AliasTarget = foreach ($_ in $AliasTarget) {
                if ($_ -ne $null) {
                    $_
                }
            }
            [PsCustomObject]@{
                Function = $FunctionName
                Alias    = $AliasTarget
            }
        }
    }
}

<#

Measure-Command {
    $Files = Get-ChildItem -LiteralPath 'C:\Support\GitHub\PSWriteHTML\Public'

    $Functions = foreach ($_ in $Files) {
        Get-AliasTarget -Path $_.FullName
    }
}




Measure-Command {
    $Files = Get-ChildItem -LiteralPath 'C:\Support\GitHub\PSWriteHTML\Public'

    $Functions = foreach ($_ in $Files) {
        [System.Management.Automation.Language.Parser]::ParseFile($_ , [ref]$null, [ref]$null)
    }

}

#>

<#
            $AliasDefinitions = $FileAst.FindAll( {
                    param ($ast)

                    $ast -is [System.Management.Automation.Language.StringConstantExpressionAst] -And $ast.Value -match '(New|Set)-Alias'
                }, $true)
            #>

#Measure-Command {
# Get-AliasTarget -Path 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1' #| Select-Object -ExpandProperty Alias
#Get-AliasTarget -path 'C:\Support\GitHub\PSPublishModule\Private\Get-AliasTarget.ps1'
# get-aliastarget -path 'C:\Support\GitHub\PSPublishModule\Private\Start-ModuleBuilding.ps1'
#}
#Get-AliasTarget -Path 'C:\Add-TableContent.ps1'

#Get-AliasTarget -Path 'C:\Support\GitHub\PSWriteHTML\Private\Add-TableContent.ps1'
#Get-FunctionNames -Path 'C:\Support\GitHub\PSWriteHTML\Private\Add-TableContent.ps1'

#Get-FunctionAliases -Path 'C:\Support\GitHub\PSSharedGoods\Public\Objects\Format-Stream.ps1'