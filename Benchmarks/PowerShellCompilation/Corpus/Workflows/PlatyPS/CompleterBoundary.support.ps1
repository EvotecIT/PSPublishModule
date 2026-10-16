# Test-only side module for the optional TabExpansionPlusPlus contract. The pinned
# module's condition, declaration, authored callback, and registration remain intact.
# Only the unavailable optional provider is replaced; this is not third-party package proof.
function Invoke-PlatyAlternateCompleter {
    param([string] $ModulePath, [string] $WorkRoot, [string] $WordToComplete, [switch] $ExpectEmpty)
    Remove-Module platyPS -ErrorAction Stop
    Remove-Module TabExpansionPlusPlus -ErrorAction SilentlyContinue
    $providerPath = Join-Path $WorkRoot 'completer-provider/TabExpansionPlusPlus.psm1'
    Write-Fixture $providerPath @'
$script:registrations = New-Object 'Collections.Generic.List[object]'
$script:completionCalls = 0
function Register-ArgumentCompleter {
    param([string[]] $CommandName, [string] $ParameterName, [scriptblock] $ScriptBlock, [string] $Description)
    $script:registrations.Add([ordered]@{ commands = @($CommandName); parameter = $ParameterName; description = $Description })
    Microsoft.PowerShell.Core\Register-ArgumentCompleter -CommandName $CommandName -ParameterName $ParameterName -ScriptBlock $ScriptBlock
}
function New-CompletionResult {
    param([string] $CompletionText, [string] $ToolTip)
    $script:completionCalls++
    if ([string]::IsNullOrEmpty($ToolTip)) { $ToolTip = $CompletionText }
    [Management.Automation.CompletionResult]::new($CompletionText, $CompletionText, [Management.Automation.CompletionResultType]::ParameterValue, $ToolTip)
}
Export-ModuleMember -Function Register-ArgumentCompleter, New-CompletionResult
'@
    $provider = Import-Module -Name $providerPath -Force -PassThru -ErrorAction Stop
    $script:module = Import-Module -Name $ModulePath -Force -PassThru -ErrorAction Stop
    $registered = @(& $provider { $script:registrations.ToArray() })
    if ($registered.Count -ne 1 -or $registered[0].parameter -cne 'Module' -or
        $registered[0].commands.Count -ne 1 -or $registered[0].commands[0] -cne 'New-MarkdownHelp' -or
        $registered[0].description -cne 'This argument completer handles the -Module parameter of the New-MarkdownHelp Command.') {
        throw 'The intact optional-provider registration branch did not execute.'
    }
    $direct = @(& $module { param($word) ModuleNameCompleter 'New-MarkdownHelp' 'Module' $word $null @{} } $WordToComplete)
    $inputText = 'New-MarkdownHelp -Module ' + $WordToComplete
    $completion = TabExpansion2 -inputScript $inputText -cursorColumn $inputText.Length
    $matches = @($completion.CompletionMatches)
    $calls = & $provider { $script:completionCalls }
    if ($ExpectEmpty) {
        if ($direct.Count -ne 0 -or $matches.Count -ne 0 -or $calls -ne 0) { throw 'The optional authored completer did not return an empty unmatched prefix.' }
    } else {
        if ($direct.Count -ne 1 -or $direct[0] -isnot [Management.Automation.CompletionResult] -or
            $direct[0].CompletionText -cne 'PlatyWorkflowFixture' -or
            $matches.Count -ne 1 -or $matches[0].CompletionText -cne 'PlatyWorkflowFixture' -or $calls -ne 2) {
            throw 'The optional authored completer was not invoked directly and through the registered callback.'
        }
    }
    [ordered]@{
        provider = 'Owned minimal optional-provider boundary, inbox completion registration'
        registration = $registered[0]
        providerCompletionCalls = $calls
        direct = @($direct | ForEach-Object { [ordered]@{ text = $_.CompletionText; list = $_.ListItemText; type = $_.ResultType.ToString(); tooltip = $_.ToolTip } })
        completion = @($matches | ForEach-Object { [ordered]@{ text = $_.CompletionText; list = $_.ListItemText; type = $_.ResultType.ToString(); tooltip = $_.ToolTip } })
    }
}
