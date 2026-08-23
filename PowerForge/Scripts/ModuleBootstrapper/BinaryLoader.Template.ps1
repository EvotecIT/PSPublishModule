{{BinaryAssemblyResolverBlock}}

{{DesktopAssemblyResolverBlock}}{{RuntimeHandlerBlock}}$PowerForgeDesktopLibrariesLoaded = $false
if ($PSEdition -ne 'Core') {
    $LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, '{{ModuleName}}.Libraries.ps1')
    if (Test-Path -LiteralPath $LibrariesScript) {
        try {
            . $LibrariesScript
            $PowerForgeDesktopLibrariesLoaded = $true
        } catch {
            if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                & $UnregisterPowerForgeDesktopAssemblyResolver
            }
            throw
        }
    }
}
$ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
foreach ($Library in $LibraryFileNames) {
    try {
        $ResolvedModuleAssembly = & $ResolvePowerForgeModuleAssembly -LibraryFileName $Library
        $ModuleAssemblyPath = $ResolvedModuleAssembly.Path
        $LibraryName = [IO.Path]::GetFileNameWithoutExtension($ModuleAssemblyPath)
        $Class = "$LibraryName.Initialize"

        if (-not ($Class -as [type])) {
            & $ImportModule $ModuleAssemblyPath -ErrorAction Stop
        } else {
            $Type = "$Class" -as [Type]
            & $ImportModule -Force -Assembly ($Type.Assembly)
        }
    } catch {
        if ($ErrorActionPreference -eq 'Stop') {
            if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                & $UnregisterPowerForgeDesktopAssemblyResolver
            }
            throw
        } else {
            Write-Warning -Message "Importing module $Library failed. Fix errors before continuing. Error: $($_.Exception.Message)"
        }
    }
}

# Dot source all libraries by loading external file
$LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, '{{ModuleName}}.Libraries.ps1')
if (-not $PowerForgeDesktopLibrariesLoaded -and (Test-Path -LiteralPath $LibrariesScript)) {
    try {
        . $LibrariesScript
    } catch {
        if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
            & $UnregisterPowerForgeDesktopAssemblyResolver
        }
        throw
    }
}
if ($PSEdition -ne 'Core' -and $null -ne $PowerForgeDesktopAssemblyResolverState) {
    $PowerForgeDesktopAssemblyResolverState.BootstrapActive = $false
    if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
        & $UnregisterPowerForgeDesktopAssemblyResolver
    }
}
