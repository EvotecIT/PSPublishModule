function New-DLLResolveConflict {
    [CmdletBinding()]
    param(
        [string] $ProjectName
    )
    if ($ProjectName) {
        $StandardName = "'$ProjectName'"
    } else {
        $StandardName = '$myInvocation.MyCommand.Name.Replace(".psm1", "")'
    }
    $Output = @"

    # Get library name, from the PSM1 file name
    `$LibraryName = $StandardName
    `$Library = "`$LibraryName.dll"
    `$Class = "`$LibraryName.Initialize"

    `$AssemblyFolders = Get-ChildItem -Path $PSScriptRoot -Directory -ErrorAction SilentlyContinue

    try {
        `$ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core

        if (`$AssemblyFolders.BaseName -contains 'Standard') {
            `$Framework = 'Standard'
        } else {
            if (`$PSEdition -eq 'Core') {
                `$Framework = 'Core'
            } else {
                `$Framework = 'Default'
            }
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