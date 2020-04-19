Function Test-ScriptFile {
    <#
    .Synopsis

    Test a PowerShell script for cmdlets

    .Description

    This command will analyze a PowerShell script file and display a list of detected commands such as PowerShell cmdlets and functions. Commands will be compared to what is installed locally. It is recommended you run this on a Windows 8.1 client with the latest version of RSAT installed. Unknown commands could also be internally defined functions. If in doubt view the contents of the script file in the PowerShell ISE or a script editor.
    You can test any .ps1, .psm1 or .txt file.
    .Parameter Path

    The path to the PowerShell script file. You can test any .ps1, .psm1 or .txt file.

    .Example

    PS C:\> test-scriptfile C:\scripts\Remove-MyVM2.ps1

    CommandType Name                                   ModuleName
    ----------- ----                                   ----------
        Cmdlet Disable-VMEventing                      Hyper-V
        Cmdlet ForEach-Object                          Microsoft.PowerShell.Core
        Cmdlet Get-VHD                                 Hyper-V
        Cmdlet Get-VMSnapshot                          Hyper-V
        Cmdlet Invoke-Command                          Microsoft.PowerShell.Core
        Cmdlet New-PSSession                           Microsoft.PowerShell.Core
        Cmdlet Out-Null                                Microsoft.PowerShell.Core
        Cmdlet Out-String                              Microsoft.PowerShell.Utility
        Cmdlet Remove-Item                             Microsoft.PowerShell.Management
        Cmdlet Remove-PSSession                        Microsoft.PowerShell.Core
        Cmdlet Remove-VM                               Hyper-V
        Cmdlet Remove-VMSnapshot                       Hyper-V
        Cmdlet Write-Debug                             Microsoft.PowerShell.Utility
        Cmdlet Write-Verbose                           Microsoft.PowerShell.Utility
        Cmdlet Write-Warning                           Microsoft.PowerShell.Utility

    .EXAMPLE

    PS C:\> Test-ScriptFile -Path 'C:\Users\przemyslaw.klys\Documents\WindowsPowerShell\Modules\PSWinReportingV2\PSWinReportingV2.psm1' | Sort-Object -Property Source, Name | ft -AutoSize

    .Notes

    Original script provided by Jeff Hicks at (https://www.petri.com/powershell-problem-solver-find-script-commands) and https://twitter.com/donnie_taylor/status/1160920407031058432

    #>

    [cmdletbinding()]
    Param(
        [Parameter(Position = 0, Mandatory = $True, HelpMessage = "Enter the path to a PowerShell script file,",
            ValueFromPipeline = $True, ValueFromPipelineByPropertyName = $True)]
        [ValidatePattern( "\.(ps1|psm1|txt)$")]
        [ValidateScript( { Test-Path $_ })]
        [string]$Path
    )

    Begin {
        Write-Verbose "Starting $($MyInvocation.Mycommand)"
        Write-Verbose "Defining AST variables"
        New-Variable astTokens -force
        New-Variable astErr -force
    }

    Process {
        Write-Verbose "Parsing $path"
        $null = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$astTokens, [ref]$astErr)

        #group tokens and turn into a hashtable
        $h = $astTokens | Group-Object tokenflags -AsHashTable -AsString

        $commandData = $h.CommandName | Where-Object { $_.text -notmatch "-TargetResource$" } |
        ForEach-Object {
            Write-Verbose "Processing $($_.text)"
            Try {
                $cmd = $_.Text
                $resolved = $cmd | Get-Command -ErrorAction Stop
                if ($resolved.CommandType -eq 'Alias') {
                    Write-Verbose "Resolving an alias"
                    #manually handle "?" because Get-Command and Get-Alias won't.
                    Write-Verbose "Detected the Where-Object alias '?'"
                    if ($cmd -eq '?') {
                        Get-Command Where-Object
                    } else {
                        # Since we're dealing with alias we need to recheck
                        $Resolved = $resolved.ResolvedCommandName | Get-Command

                        [PSCustomobject]@{
                            CommandType = $resolved.CommandType
                            Name        = $resolved.Name
                            ModuleName  = $resolved.ModuleName
                            Source      = $resolved.Source
                        }
                    }
                } else {
                    #$resolved

                    [PSCustomobject]@{
                        CommandType = $resolved.CommandType
                        Name        = $resolved.Name
                        ModuleName  = $resolved.ModuleName
                        Source      = $resolved.Source
                    }
                }
            } Catch {
                Write-Verbose "Command is not recognized"
                #create a custom object for unknown commands
                [PSCustomobject]@{
                    CommandType = "Unknown"
                    Name        = $cmd
                    ModuleName  = "Unknown"
                    Source      = "Unknown"
                }
            }
        }

        $CommandData
    }

    End {
        Write-Verbose -Message "Ending $($MyInvocation.Mycommand)"
    }
}