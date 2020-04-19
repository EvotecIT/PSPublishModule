function Get-FilteredScriptCommands {
    [CmdletBinding()]
    param(
        [Array] $Commands,
        [switch] $NotCmdlet,
        [switch] $NotUnknown,
        [switch] $NotApplication,
        [string[]] $Functions,
        [string] $FilePath
    )
    if ($Functions.Count -eq 0) {
        $Functions = Get-FunctionNames -Path $FilePath
    }
    $Commands = $Commands | Where-Object { $_ -notin $Functions }
    $Commands = $Commands | Sort-Object -Unique
    $Scan = foreach ($Command in $Commands) {
        try {
            $IsAlias = $false
            $Data = Get-Command -Name $Command -ErrorAction Stop
            if ($Data.CommandType -eq 'Alias') {
                $Data = Get-Command -Name $Data.Definition
                $IsAlias = $true
            }
            [PSCustomObject] @{
                Name        = $Data.Name
                Source      = $Data.Source
                CommandType = $Data.CommandType
                IsAlias     = $IsAlias
                Error       = ''
                ScriptBlock = $Data.ScriptBlock
            }
        } catch {
            [PSCustomObject] @{
                Name        = $Command
                Source      = ''
                CommandType = ''
                IsAlias     = $IsAlias
                Error       = $_.Exception.Message
                ScriptBlock = ''
            }
        }
    }
    $Filtered = foreach ($Command in $Scan) {
        if ($Command.Source -eq 'Microsoft.PowerShell.Core') {
            # skipping because otherwise Import-Module fails if part of RequiredModules
            continue
        }
        if ($NotCmdlet -and $NotUnknown -and $NotApplication) {
            if ($Command.CommandType -ne 'Cmdlet' -and $Command.Source -ne '' -and $Command.CommandType -ne 'Application') {
                $Command
            }
        } elseif ($NotCmdlet -and $NotUnknown) {
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
        } elseif ($NotApplication) {
            if ($Command.CommandType -ne 'Application') {
                $Command
            }
        } else {
            $Command
        }
    }
    $Filtered
}
