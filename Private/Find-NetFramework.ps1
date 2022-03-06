function Find-NetFramework {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER RequireVersion
    Parameter description

    .EXAMPLE
    Find-NetFramework -RequireVersion 4.8

    .NOTES
    General notes
    #>
    [cmdletBinding()]
    param(
        [string] $RequireVersion
    )
    if ($RequireVersion) {
        $Framework = [ordered] @{
            '4.5'   = 378389
            '4.5.1'	= 378675
            '4.5.2'	= 379893
            '4.6'   = 393295
            '4.6.1'	= 394254
            '4.6.2'	= 394802
            '4.7'   = 460798
            '4.7.1'	= 461308
            '4.7.2'	= 461808
            '4.8'   = 528040
        }
        $DetectVersion = $Framework[$RequireVersion]

        "if (`$PSVersionTable.PSEdition -eq 'Desktop' -and (Get-ItemProperty `"HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full`").Release -lt $DetectVersion) { Write-Warning `"This module requires .NET Framework $RequireVersion or later.`"; return } "
    }
}