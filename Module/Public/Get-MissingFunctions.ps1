function Get-MissingFunctions {
    <#
    .SYNOPSIS
    Analyzes a script or scriptblock and reports functions it calls that are not present.

    .DESCRIPTION
    Scans the provided file path or scriptblock, detects referenced commands, filters them down to
    function calls, and returns a summary or the raw helper function definitions that can be inlined.
    When -ApprovedModules is specified, helper definitions are only taken from those modules; otherwise
    only the list is returned. Use this to build self-contained scripts by discovering dependencies.

    .PARAMETER FilePath
    Path to a script file to analyze for missing function dependencies. Alias: Path.

    .PARAMETER Code
    ScriptBlock to analyze instead of a file. Alias: ScriptBlock.

    .PARAMETER Functions
    Known function names to treat as already available (exclude from missing list).

    .PARAMETER Summary
    Return only a flattened summary list of functions used (objects with Name/Source), not inlined definitions.

    .PARAMETER SummaryWithCommands
    Return a hashtable with Summary (names), SummaryFiltered (objects), and Functions (inlineable text).

    .PARAMETER ApprovedModules
    Module names that are allowed sources for pulling inline helper function definitions.

    .PARAMETER IgnoreFunctions
    Function names to ignore when computing the missing set.

    .EXAMPLE
    Get-MissingFunctions -FilePath .\Build\Manage-Module.ps1 -Summary
    Returns a list of functions used by the script.

    .EXAMPLE
    $sb = { Invoke-ModuleBuild -ModuleName 'MyModule' }
    Get-MissingFunctions -Code $sb -SummaryWithCommands -ApprovedModules 'PSSharedGoods','PSPublishModule'
    Returns a hashtable with a summary and inlineable helper definitions sourced from approved modules.

    .NOTES
    Use with Initialize-PortableScript to emit a self-contained version of a script.
    #>
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
        $CommandsUsedInCode = Get-ScriptCommands -FilePath $FilePath
    } elseif ($Code) {
        $CommandsUsedInCode = Get-ScriptCommands -Code $Code
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
    [Array] $FilteredCommands = Get-FilteredScriptCommands -Commands $Result -Functions $Functions -FilePath $FilePath -ApprovedModules $ApprovedModules
    foreach ($_ in $FilteredCommands) {
        $ListCommands.Add($_)
    }
    # Ensures even one object is array
    [Array] $FilteredCommandsName = foreach ($Name in $FilteredCommands.Name) {
        $Name
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
        $IgnoreAlreadyKnownCommands = ($FilteredCommandsName + $IgnoreFunctions) | Sort-Object -Unique
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
