function Get-FilteredScriptCommands {
    [CmdletBinding()]
    param(
        [Array] $Commands,
        [switch] $NotCmdlet,
        [switch] $NotUnknown,
        [switch] $NotApplication,
        [string[]] $Functions,
        [string] $FilePath,
        [string[]] $ApprovedModules
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
            if ($Data.Source -eq 'PSPublishModule') {
                # we need to exclude PSPublishModule from any processing
                # this is because it has advantage of being in the same scope
                # this means it's functions would be preferred over any other
                if ($Data.Source -notin $ApprovedModules) {
                    throw
                }
            }
            if ($Data.CommandType -eq 'Alias') {
                $Data = Get-Command -Name $Data.Definition
                $IsAlias = $true
            }
            [PSCustomObject] @{
                Name        = $Data.Name
                Source      = $Data.Source
                CommandType = $Data.CommandType
                IsAlias     = $IsAlias
                IsPrivate   = $false
                Error       = ''
                ScriptBlock = $Data.ScriptBlock
            }
        } catch {
            $CurrentOutput = [PSCustomObject] @{
                Name        = $Command
                Source      = ''
                CommandType = ''
                IsAlias     = $IsAlias
                IsPrivate   = $false
                Error       = $_.Exception.Message
                ScriptBlock = ''
            }
            # So we caught exception, we know the command doesn't exists
            # so now we check if it's one of the private commands from Approved Modules
            # this will allow us to integrate it regardless how it's done.
            foreach ($ApprovedModule in $ApprovedModules) {
                try {
                    $ImportModuleWithPrivateCommands = Import-Module -PassThru -Name $ApprovedModule -ErrorAction Stop -Verbose:$false
                    $Data = & $ImportModuleWithPrivateCommands { param($command); Get-Command $command -Verbose:$false -ErrorAction Stop } $command
                    $CurrentOutput = [PSCustomObject] @{
                        Name        = $Data.Name
                        Source      = $Data.Source
                        CommandType = $Data.CommandType
                        IsAlias     = $IsAlias
                        IsPrivate   = $true
                        Error       = ''
                        ScriptBlock = $Data.ScriptBlock
                    }
                    break
                } catch {
                    $CurrentOutput = [PSCustomObject] @{
                        Name        = $Command
                        Source      = ''
                        CommandType = ''
                        IsAlias     = $IsAlias
                        IsPrivate   = $false
                        Error       = $_.Exception.Message
                        ScriptBlock = ''
                    }
                }
            }
            $CurrentOutput
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
