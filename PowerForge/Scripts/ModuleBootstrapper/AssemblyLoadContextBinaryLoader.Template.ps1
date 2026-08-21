# Get library name, from the PSM1 file name
$LibraryName = '{{LibraryName}}'
$Library = "$LibraryName.dll"
$Class = "$LibraryName.Initialize"

$LibRoot = [IO.Path]::Combine($PowerForgeModuleRoot, 'Lib')
$AssemblyFolders = Get-ChildItem -LiteralPath $LibRoot -Directory -ErrorAction SilentlyContinue

$Default = $false
$Core = $false
$Standard = $false
$HasNamedCorePayload = $false
foreach ($A in $AssemblyFolders.Name) {
    if ($A -eq 'Default') {
        $Default = $true
    } elseif ($A -eq 'Core') {
        $Core = $true
    } elseif ($A -eq 'Standard') {
        $Standard = $true
    } elseif ($A -match '^Core-(?:net|netcoreapp)\d+\.\d+$') {
        $HasNamedCorePayload = $true
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
    $Framework = 'Default'
    $FrameworkNet = 'Default'
} elseif ($HasNamedCorePayload -and $PSEdition -eq 'Core') {
    $Framework = ''
    $FrameworkNet = ''
} else {
    Write-Error -Message 'No assemblies found'
    return
}

{{RuntimePayloadSelectorBlock}}
if ($PSEdition -eq 'Core' -and $HasNamedCorePayload -and [string]::IsNullOrWhiteSpace($Framework)) {
    Write-Error -Message 'No compatible PowerShell Core assemblies found'
    return
}
if ($PSEdition -eq 'Core') {
    $LibFolder = $Framework
} else {
    $LibFolder = $FrameworkNet
}

{{DesktopAssemblyResolverBlock}}{{RuntimeHandlerBlock}}if ($PSEdition -ne 'Core') {
    $LibrariesScript = [IO.Path]::Combine($PowerForgeModuleRoot, '{{ModuleName}}.Libraries.ps1')
    if (Test-Path -LiteralPath $LibrariesScript) {
        try {
            . $LibrariesScript
        } catch {
            if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                & $UnregisterPowerForgeDesktopAssemblyResolver
            }
            throw
        }
    }
}
$PowerForgeDesktopBinaryLoaded = $false
try {
    $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
    $ModuleAssemblyPath = [IO.Path]::Combine($LibRoot, $LibFolder, $Library)

    if ($PSEdition -eq 'Core') {
        $LoaderAssemblyPath = [IO.Path]::Combine($LibRoot, $LibFolder, '{{LoaderAssemblyName}}.dll')
        if (-not ('{{LoaderTypeName}}' -as [type])) {
            Add-Type -Path $LoaderAssemblyPath -ErrorAction Stop
        }

        $ModuleAssembly = [{{LoaderTypeName}}]::LoadModule($ModuleAssemblyPath, '{{ModuleName}}')
        $InnerModule = & $ImportModule -Assembly $ModuleAssembly -Force -PassThru -ErrorAction Stop

{{TypeAcceleratorBlock}}
        if ($InnerModule) {
{{ExportBridgeBlock}}
        }
    } elseif (-not ($Class -as [type])) {
        & $ImportModule $ModuleAssemblyPath -ErrorAction Stop
    } else {
        $Type = "$Class" -as [Type]
        & $ImportModule -Force -Assembly ($Type.Assembly)
    }

    if ($PSEdition -ne 'Core') {
        $PowerForgeDesktopBinaryLoaded = $true
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

if ($PSEdition -ne 'Core' -and $PowerForgeDesktopBinaryLoaded) {
{{DesktopTypeAcceleratorBlock}}
}
if ($PSEdition -ne 'Core' -and $null -ne $PowerForgeDesktopAssemblyResolverState) {
    $PowerForgeDesktopAssemblyResolverState.BootstrapActive = $false
    if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
        & $UnregisterPowerForgeDesktopAssemblyResolver
    }
}
