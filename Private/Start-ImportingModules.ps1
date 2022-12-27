function Start-ImportingModules {
    [CmdletBinding()]
    param(
        [string] $ProjectName,
        [System.Collections.IDictionary] $Configuration
    )
    $TemporaryVerbosePreference = $VerbosePreference
    $VerbosePreference = $false
    if ($Configuration.Steps.ImportModules.RequiredModules) {
        Write-TextWithTime -Text '[+] Importing modules - REQUIRED' {
            foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                Import-Module -Name $Module -Force -ErrorAction Stop -Verbose:$false
            }
        }
    }
    if ($Configuration.Steps.ImportModules.Self) {
        Write-TextWithTime -Text '[+] Importing module - SELF' {
            Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$false
        }
    }
    $VerbosePreference = $TemporaryVerbosePreference
}