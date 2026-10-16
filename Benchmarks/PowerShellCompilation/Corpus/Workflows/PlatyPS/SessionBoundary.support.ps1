function Invoke-PlatySessionBoundary {
    param([Parameter(Mandatory=$true)]$Module,[Parameter(Mandatory=$true)][string]$OutputFolder,[switch]$FailTransport)
    $runspace=$null; $session=$null; $providerInstalled=$false
    try {
        $runspace=[Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace((New-Object Management.Automation.Runspaces.WSManConnectionInfo))
        $constructors=@([Management.Automation.Runspaces.PSSession].GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.IsInstanceOfType($runspace)})
        if($constructors.Count -ne 1){throw 'Unopened session constructor is ambiguous'}
        $session=$constructors[0].Invoke([object[]]@($runspace)); $session.Name='owned-offline-platy-session'
        $trace=New-Object 'Collections.Generic.List[object]'
        & $Module {
            param($Expected,$Trace,[bool]$Failure)
            if(Get-Command Invoke-Command -CommandType Function -ErrorAction Ignore){throw 'Unexpected pre-existing module function Invoke-Command'}
            foreach($name in @('OwnedPlatyExpectedSession','OwnedPlatyTransportTrace','OwnedPlatyTransportFailure','OwnedPlatyProviderScriptBlock')){if(Get-Variable -Name $name -Scope Script -ErrorAction Ignore){throw 'Unexpected pre-existing observer variable'}}
            $script:OwnedPlatyExpectedSession=$Expected
            $script:OwnedPlatyTransportTrace=$Trace
            $script:OwnedPlatyTransportFailure=$Failure
            function script:Invoke-Command {
                [CmdletBinding()]
                param([Parameter(Position=0)][Management.Automation.Runspaces.PSSession]$Session,[Parameter(Position=1)][scriptblock]$ScriptBlock)
                if(-not[object]::ReferenceEquals($Session,$script:OwnedPlatyExpectedSession)){throw 'Offline provider lost borrowed session identity'}
                if($Session.Runspace.RunspaceStateInfo.State.ToString() -ne 'BeforeOpen'){throw 'Offline provider refuses any opened session'}
                $statements=@($ScriptBlock.Ast.EndBlock.Statements)
                if($statements.Count -ne 1 -or $statements[0] -isnot [Management.Automation.Language.PipelineAst]){throw 'Unexpected transport script statement shape'}
                $commands=@($statements[0].PipelineElements)
                if($commands.Count -lt 1 -or $commands.Count -gt 5 -or $commands[0].GetCommandName() -ne 'Get-Command' -or $commands[0].CommandElements.Count -ne 2){throw ('Unexpected transport command shape: pipeline='+$commands.Count)}
                $captured=[ordered]@{}
                foreach($usingAst in @($ScriptBlock.Ast.FindAll({param($n)$n -is [Management.Automation.Language.UsingExpressionAst]},$true))){
                    if($usingAst.SubExpression -isnot [Management.Automation.Language.VariableExpressionAst]){throw 'Unexpected transport using expression'}
                    $name=$usingAst.SubExpression.VariablePath.UserPath -replace '^using:',''
                    if($name -notin @('Cmdlet','property','property1','property2','property3','num')){throw 'Unexpected transport variable name'}
                    $found=$null
                    for($scope=1;$scope -le 8 -and $null -eq $found;$scope++){
                        try{$found=Get-Variable -Name $name -Scope $scope -ErrorAction Stop}catch{}
                    }
                    if($null -eq $found){throw ('Offline boundary cannot observe using variable '+$name)}
                    $captured[$name]=$found.Value
                }
                if($captured.Cmdlet -ne 'Get-PlatyFixture'){throw 'Unexpected offline metadata command'}
                if($commands[0].CommandElements[1] -isnot [Management.Automation.Language.UsingExpressionAst]){throw 'Transport command identity is not captured'}
                $selected=@(Microsoft.PowerShell.Core\Get-Command -Name $captured.Cmdlet -ErrorAction Stop)
                $steps=New-Object 'Collections.Generic.List[object]'
                foreach($command in @($commands|Select-Object -Skip 1)){
                    if($command -isnot [Management.Automation.Language.CommandAst] -or $command.GetCommandName() -ne 'Select-Object'){throw 'Unexpected offline metadata pipeline command'}
                    $elements=@($command.CommandElements)
                    if($elements.Count -lt 3 -or $elements.Count -gt 4 -or $elements[1] -isnot [Management.Automation.Language.CommandParameterAst] -or $elements[2] -isnot [Management.Automation.Language.UsingExpressionAst]){throw 'Unexpected metadata projection shape'}
                    $name=$elements[2].SubExpression.VariablePath.UserPath -replace '^using:',''
                    $value=$captured[$name]
                    if($elements[1].ParameterName -eq 'ExpandProperty'){
                        if($value -notin @('ParameterSets','Parameters','ParameterType')){throw 'Unexpected metadata property'}
                        $selected=@($selected|Microsoft.PowerShell.Utility\Select-Object -ExpandProperty $value)
                        $steps.Add([ordered]@{operation='expand';property=$value})
                    }elseif($elements[1].ParameterName -eq 'Index'){
                        if($elements.Count -ne 4 -or $elements[3].ParameterName -ne 'Wait' -or [int]$value -lt 0){throw 'Unexpected metadata index'}
                        $selected=@($selected|Microsoft.PowerShell.Utility\Select-Object -Index ([int]$value) -Wait)
                        $steps.Add([ordered]@{operation='index';index=[int]$value})
                    }else{throw 'Unexpected offline metadata projection'}
                }
                $script:OwnedPlatyTransportTrace.Add([ordered]@{ordinal=$script:OwnedPlatyTransportTrace.Count;usingValues=$captured;steps=$steps.ToArray();resultCount=$selected.Count;sessionIdentity=$true;state='BeforeOpen'})
                if($script:OwnedPlatyTransportFailure){Write-Error 'Owned offline transport failure' -ErrorId OwnedPlatyTransportFailure; return}
                # Bound provider data to the public fields this authored request asks for.
                # Never serialize live CommandInfo, ModuleInfo, reflection or runspace graphs.
                if($selected.Count -gt 64){throw 'Offline metadata row limit exceeded'}
                $kind=if($commands.Count -eq 1){'Command'}else{$steps[$steps.Count-1].property}
                foreach($item in $selected){
                    switch($kind){
                        'Command' {[pscustomobject][ordered]@{Name=$item.Name;CommandType=$item.CommandType.ToString();DefaultParameterSet=$item.DefaultParameterSet;CmdletBinding=[bool]$item.CmdletBinding;ModuleName=$item.ModuleName}}
                        'ParameterSets' {[pscustomobject][ordered]@{Name=$item.Name;IsDefault=[bool]$item.IsDefault}}
                        'Parameters' {
                            $attributes=@(foreach($attribute in $item.Attributes){[pscustomobject][ordered]@{TypeId=$attribute.GetType().FullName;DontShow=if($attribute -is [Management.Automation.ParameterAttribute]){[bool]$attribute.DontShow}else{$false}}})
                            [pscustomobject][ordered]@{Name=$item.Name;IsMandatory=[bool]$item.IsMandatory;Position=$item.Position;ValueFromPipeline=[bool]$item.ValueFromPipeline;ValueFromPipelineByPropertyName=[bool]$item.ValueFromPipelineByPropertyName;ValueFromRemainingArguments=[bool]$item.ValueFromRemainingArguments;Aliases=@($item.Aliases);HelpMessage=$item.HelpMessage;ParameterType=$item.ParameterType.ToString();Attributes=$attributes}
                        }
                        'ParameterType' {[pscustomobject][ordered]@{Name=$item.Name;IsGenericType=[bool]$item.IsGenericType;GenericTypeArguments=@($item.GenericTypeArguments|ForEach-Object Name)}}
                        default {throw 'Unexpected offline metadata result kind'}
                    }
                }
            }
            $script:OwnedPlatyProviderScriptBlock=[string](Get-Command Invoke-Command -CommandType Function -ErrorAction Stop).Definition
        } $session $trace ([bool]$FailTransport)
        $providerInstalled=$true
        New-MarkdownHelp -Command Get-PlatyFixture -Session $session -OutputFolder $OutputFolder -AlphabeticParamsOrder -UseFullTypeName -ExcludeDontShow
        if($runspace.RunspaceStateInfo.State.ToString() -ne 'BeforeOpen'){throw 'Qualification opened a transport'}
        [ordered]@{boundary='Module-scoped Invoke-Command only; real MyGetCommand and all nested metadata helpers retained';networkOpened=$false;state=$runspace.RunspaceStateInfo.State.ToString();calls=$trace.ToArray()}
    } finally {
        if($Module -and $providerInstalled){
            & $Module {
                $current=Get-Command Invoke-Command -CommandType Function -ErrorAction Ignore
                if($current -and [string]$current.Definition -cne [string]$script:OwnedPlatyProviderScriptBlock){throw 'Owned offline provider changed before cleanup'}
                if(Test-Path -LiteralPath 'Function:\Invoke-Command'){Remove-Item -LiteralPath 'Function:\Invoke-Command'}
                if(Get-Command Invoke-Command -CommandType Function -ErrorAction Ignore){throw 'Owned offline provider remains after cleanup'}
                Remove-Variable -Name OwnedPlatyExpectedSession,OwnedPlatyTransportTrace,OwnedPlatyTransportFailure,OwnedPlatyProviderScriptBlock -Scope Script -ErrorAction SilentlyContinue
            }
        }
        if($runspace){$runspace.Dispose()}
    }
}
