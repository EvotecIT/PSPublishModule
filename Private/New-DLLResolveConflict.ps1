function New-DLLResolveConflict {
    [CmdletBinding()]
    param(
        [string] $ProjectName
    )
    if ($ProjectName) {
        $StandardName = "'$ProjectName'"
    } else {
        $StandardName = '`$myInvocation.MyCommand.Name.Replace(".psm1", "")'
    }
    $Output = @"

    # Get library name, from the PSM1 file name
    `$LibraryName = $StandardName
    `$Library = "`$LibraryName.dll"
    `$Class = "`$LibraryName.Initialize"

    try {
        `$ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
        `$Framework = if (`$PSVersionTable.PSVersion.Major -eq 5) {
            'Default'
        } else {
            'Core'
        }
        if (-not (`$Class -as [type])) {
            & `$ImportModule ([IO.Path]::Combine(`$PSScriptRoot, 'Lib', `$Framework, `$Library)) -ErrorAction Stop
        } else {
            `$Type = "`$Class" -as [Type]
            & `$importModule -Force -Assembly (`$Type.Assembly)
        }
    } catch {
        Write-Warning -Message "Importing module `$Library failed. Fix errors before continuing. Error: `$(`$_.Exception.Message)"
        `$true
    }
"@
    $Output

}