---
external help file: PSPublishModule-help.xml
Module Name: PSPublishModule
online version:
schema: 2.0.0
---

# Test-ScriptFile

## SYNOPSIS
Test a PowerShell script for cmdlets

## SYNTAX

```
Test-ScriptFile [-Path] <String> [<CommonParameters>]
```

## DESCRIPTION
This command will analyze a PowerShell script file and display a list of detected commands such as PowerShell cmdlets and functions.
Commands will be compared to what is installed locally.
It is recommended you run this on a Windows 8.1 client with the latest version of RSAT installed.
Unknown commands could also be internally defined functions.
If in doubt view the contents of the script file in the PowerShell ISE or a script editor.
You can test any .ps1, .psm1 or .txt file.

## EXAMPLES

### EXAMPLE 1
```
test-scriptfile C:\scripts\Remove-MyVM2.ps1
```

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

## PARAMETERS

### -Path
The path to the PowerShell script file.
You can test any .ps1, .psm1 or .txt file.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

## NOTES
Original script provided by Jeff Hicks at (https://www.petri.com/powershell-problem-solver-find-script-commands)
# https://twitter.com/donnie_taylor/status/1160920407031058432
Test-ScriptFile -Path 'C:\Users\przemyslaw.klys\Documents\WindowsPowerShell\Modules\PSWinReportingV2\PSWinReportingV2.psm1' | Sort-Object -Property Source, Name | ft -AutoSize

## RELATED LINKS
