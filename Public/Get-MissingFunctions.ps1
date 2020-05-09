function Get-MissingFunctions {
    [CmdletBinding()]
    param(
        [alias('Path')][string] $FilePath,
        [alias('ScriptBlock')][scriptblock] $Code,
        [string[]] $Functions,
        [switch] $Summary,
        [switch] $SummaryWithCommands,
        [Array] $ApprovedModules,
        [Array] $IgnoreFunctions
    )
    $ListCommands = [System.Collections.Generic.List[Object]]::new()
    if ($FilePath) {
        $CommandsUsedInCode = Get-ScriptCommands -FilePath $FilePath -CommandsOnly
    } elseif ($Code) {
        $CommandsUsedInCode = Get-ScriptCommands -CommandsOnly -Code $Code
    } else {
        return
    }
    if ($IgnoreFunctions.Count -gt 0) {
        $Result = foreach ($_ in $CommandsUsedInCode) {
            if ($IgnoreFunctions -notcontains $_) {
                $_
            }
        }
    } else {
        $Result = $CommandsUsedInCode
    }
    #$FilteredCommands = Get-FilteredScriptCommands -Commands $Result -NotUnknown -NotCmdlet -Functions $Functions -NotApplication -FilePath $FilePath
    $FilteredCommands = Get-FilteredScriptCommands -Commands $Result -Functions $Functions -FilePath $FilePath -ApprovedModules $ApprovedModules
    foreach ($_ in $FilteredCommands) {
        $ListCommands.Add($_)
    }
    # this gets commands along their ScriptBlock
    # $FilteredCommands = Get-RecursiveCommands -Commands $FilteredCommands
    [Array] $FunctionsOutput = foreach ($_ in $ListCommands) {
        if ($_.ScriptBlock) {
            if ($ApprovedModules.Count -gt 0 -and $_.Source -in $ApprovedModules) {
                "function $($_.Name) { $($_.ScriptBlock) }"
            } elseif ($ApprovedModules.Count -eq 0) {
                #"function $($_.Name) { $($_.ScriptBlock) }"
            }
        }
    }

    if ($FunctionsOutput.Count -gt 0) {
        $IgnoreAlreadyKnownCommands = ($FilteredCommands.Name + $IgnoreFunctions) | Sort-Object -Unique
        $ScriptBlockMissing = [scriptblock]::Create($FunctionsOutput)
        $AnotherRun = Get-MissingFunctions -SummaryWithCommands -ApprovedModules $ApprovedModules -Code $ScriptBlockMissing -IgnoreFunctions $IgnoreAlreadyKnownCommands
    }

    if ($SummaryWithCommands) {
        if ($AnotherRun) {
            $Hash = @{ }
            $Hash.Summary = foreach ($_ in $FilteredCommands + $AnotherRun.Summary) {
                $_
            }
            $Hash.SummaryFiltered = foreach ($_ in $ListCommands + $AnotherRun.SummaryFiltered) {
                $_
            }
            $Hash.Functions = foreach ($_ in $FunctionsOutput + $AnotherRun.Functions) {
                $_
            }
        } else {
            $Hash = @{
                Summary         = $FilteredCommands
                SummaryFiltered = $ListCommands
                Functions       = $FunctionsOutput
            }
        }
        return $Hash
    } elseif ($Summary) {
        if ($AnotherRun) {
            foreach ($_ in $ListCommands + $AnotherRun.SummaryFiltered) {
                $_
            }
        } else {
            return $ListCommands
        }
    } else {
        return $FunctionsOutput
    }
}