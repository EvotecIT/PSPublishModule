function Get-FilteredScriptCommands {
    [CmdletBinding()]
    param(
        [Array] $Commands,
        [switch] $NotCmdlet,
        [switch] $NotUnknown,
        [string[]] $Functions
    )
    if ($Functions.Count -eq 0) {
        $Functions = Get-FunctionNames -Path $FilePath
    }
    $Commands = $Commands | Where-Object { $_ -notin $Functions }
    $Commands = $Commands | Sort-Object -Unique
    $Scan = foreach ($Command in $Commands) {
        try {
            $Data = Get-Command -Name $Command -ErrorAction Stop
            [PSCustomObject] @{
                Name        = $Data.Name
                Source      = $Data.Source
                CommandType = $Data.CommandType
                Error       = ''
                ScriptBlock = $Data.ScriptBlock
            }
        } catch {
            [PSCustomObject] @{
                Name        = $Command
                Source      = ''
                CommandType = ''
                Error       = $_.Exception.Message
                ScriptBlock = ''
            }
        }
    }
    $Filtered = foreach ($Command in $Scan) {
        if ($NotCmdlet -and $NotUnknown) {
            if ($Command.CommandType -ne 'Cmdlet' -and $Command.Source -ne '') {
                $Command
            }
        } elseif ($NotCmdlet) {
            if ($Command.CommandType -ne 'Cmdlet') {
                $Command
            }
        } elseif ($NotUnknown) {
            if ($Command.Source -ne '') {
                $Command
            }
        } else {
            $Command
        }
    }
    $Filtered
}
