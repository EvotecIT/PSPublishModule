function Start-ImportingModules {
    [CmdletBinding()]
    param(
        [string] $ProjectName,
        [System.Collections.IDictionary] $Configuration
    )
    $TemporaryVerbosePreference = $VerbosePreference
    $TemporaryErrorPreference = $global:ErrorActionPreference
    $global:ErrorActionPreference = 'Stop'

    if ($null -ne $ImportModules.Verbose) {
        $VerbosePreference = $true
    } else {
        $VerbosePreference = $false
    }
    if ($Configuration.Steps.ImportModules.RequiredModules) {
        Write-TextWithTime -Text 'Importing modules (as defined in dependencies)' {
            foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                if ($Module -is [System.Collections.IDictionary]) {
                    Write-Text "   [>] Importing required module - $($Module.ModuleName)" -Color Yellow
                    if ($Module.ModuleVersion) {
                        Import-Module -Name $Module.ModuleName -MinimumVersion $Module.ModuleVersion -Force -ErrorAction Stop -Verbose:$VerbosePreference
                    } elseif ($Module.ModuleName) {
                        Import-Module -Name $Module.ModuleName -Force -ErrorAction Stop -Verbose:$VerbosePreference
                    }
                } elseif ($Module -is [string]) {
                    Write-Text "   [>] Importing required module - $($Module)" -Color Yellow
                    Import-Module -Name $Module -Force -ErrorAction Stop -Verbose:$VerbosePreference
                }
            }
        } -PreAppend 'Information'
    }
    if ($Configuration.Steps.ImportModules.Self) {
        Write-TextWithTime -Text 'Importing module - SELF' {
            # global:Write-Warning ($Message) { throw 'Import warnings raised: $Message' }
            Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$VerbosePreference
            #Remove-Item function:Write-Warning
        } -PreAppend 'Information'
    }
    $global:ErrorActionPreference = $TemporaryErrorPreference
    $VerbosePreference = $TemporaryVerbosePreference
}