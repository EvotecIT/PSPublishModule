Function Get-AliasTarget {
    [cmdletbinding()]
    param (

        [Alias('PSPath', 'FullName')][Parameter(ValueFromPipeline, ValueFromPipelineByPropertyName)][string[]]$Path,
        [string] $Content,
        [switch] $RecurseFunctionNames,
        [switch] $RecurseAliases
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
            if ($Code) {
                $FileAst = [System.Management.Automation.Language.Parser]::ParseInput($File, [ref]$null, [ref]$null)
            } else {
                $FileAst = [System.Management.Automation.Language.Parser]::ParseFile($File , [ref]$null, [ref]$null)
            }
            [Array] $FunctionName = $FileAst.FindAll( {
                    param ($ast)
                    $ast -is [System.Management.Automation.Language.FunctionDefinitionAst]
                }, $RecurseFunctionNames).Name
            $AliasDefinitions = $FileAst.FindAll( {
                    param ( $ast )
                    $ast -is [System.Management.Automation.Language.AttributeAst] -and
                    $ast.TypeName.Name -eq 'Alias' -and
                    $ast.Parent -is [System.Management.Automation.Language.ParamBlockAst]
                }, $RecurseAliases).PositionalArguments.Value
            [Array] $AliasTarget = @(
                $AliasDefinitions.Parent.CommandElements.Where( {
                        $_.StringConstantType -eq 'BareWord' -and
                        $_.Value -notin ('New-Alias', 'Set-Alias', $FunctionName)
                    }).Value
                $Attributes = $FileAst.FindAll( {
                        param ($ast)

                        $ast -is [System.Management.Automation.Language.AttributeAst]
                    }, $true)
                $AliasDefinitions = $Attributes.Where( { $_.TypeName.Name -eq 'Alias' -and $_.Parent -is [System.Management.Automation.Language.ParamBlockAst] })
                $AliasDefinitions.PositionalArguments.Value
            )
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