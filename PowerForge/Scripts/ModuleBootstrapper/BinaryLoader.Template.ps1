# Get library name, from the PSM1 file name
$LibraryName = '{{LibraryName}}'
$Library = "$LibraryName.dll"
$Class = "$LibraryName.Initialize"

$LibRoot = [IO.Path]::Combine($PowerForgeModuleRoot, 'Lib')
$AssemblyFolders = Get-ChildItem -LiteralPath $LibRoot -Directory -ErrorAction SilentlyContinue

$Default = $false
$Core = $false
$Standard = $false
foreach ($A in $AssemblyFolders.Name) {
    if ($A -eq 'Default') {
        $Default = $true
    } elseif ($A -eq 'Core') {
        $Core = $true
    } elseif ($A -eq 'Standard') {
        $Standard = $true
    }
}
if ($Standard -and $Core -and $Default) {
    $FrameworkNet = 'Default'
    $Framework = 'Standard'
} elseif ($Standard -and $Core) {
    $Framework = 'Standard'
    $FrameworkNet = 'Standard'
} elseif ($Core -and $Default) {
    $Framework = 'Core'
    $FrameworkNet = 'Default'
} elseif ($Standard -and $Default) {
    $Framework = 'Standard'
    $FrameworkNet = 'Default'
} elseif ($Standard) {
    $Framework = 'Standard'
    $FrameworkNet = 'Standard'
} elseif ($Core) {
    $Framework = 'Core'
    $FrameworkNet = ''
} elseif ($Default) {
    $Framework = ''
    $FrameworkNet = 'Default'
} else {
    Write-Error -Message 'No assemblies found'
    return
}

if ($PSEdition -eq 'Core') {
    $LibFolder = $Framework
} else {
    $LibFolder = $FrameworkNet
}

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
try {
    $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core

    if (-not ($Class -as [type])) {
        & $ImportModule ([IO.Path]::Combine($LibRoot, $LibFolder, $Library)) -ErrorAction Stop
    } else {
        $Type = "$Class" -as [Type]
        & $importModule -Force -Assembly ($Type.Assembly)
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
