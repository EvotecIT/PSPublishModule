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
            try {
                Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$VerbosePreference
            } catch {
                Write-Text "   [i] Import by name failed. Trying installed paths for '$ProjectName'." -Color Yellow
                $roots = @()
                $docs = [Environment]::GetFolderPath('MyDocuments')
                if ($docs) {
                    $roots += (Join-Path $docs 'PowerShell\Modules')
                    $roots += (Join-Path $docs 'WindowsPowerShell\Modules')
                }
                $candidate = $null
                foreach ($r in $roots) {
                    $mr = Join-Path $r $ProjectName
                    if (Test-Path -LiteralPath $mr) {
                        $dirs = Get-ChildItem -LiteralPath $mr -Directory | Sort-Object Name -Descending
                        $candidate = ($dirs | Select-Object -First 1).FullName
                        if ($candidate) { break }
                    }
                }
                if ($candidate) {
                    $psd1 = Join-Path $candidate ("{0}.psd1" -f $ProjectName)
                    $psm1 = Join-Path $candidate ("{0}.psm1" -f $ProjectName)
                    $pathToImport = if (Test-Path -LiteralPath $psd1) { $psd1 } elseif (Test-Path -LiteralPath $psm1) { $psm1 } else { $null }
                    if ($pathToImport) {
                        # Use -Name for broad compatibility across hosts
                        Import-Module -Name $pathToImport -Force -ErrorAction Stop -Verbose:$VerbosePreference
                    } else {
                        throw "Neither PSD1 nor PSM1 found under '$candidate'."
                    }
                } else {
                    throw "Module '$ProjectName' not found under user module roots."
                }
            }
        } -PreAppend 'Information'
    }
    $global:ErrorActionPreference = $TemporaryErrorPreference
    $VerbosePreference = $TemporaryVerbosePreference
}
