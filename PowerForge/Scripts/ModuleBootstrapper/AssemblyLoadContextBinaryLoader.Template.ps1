{{BinaryAssemblyResolverBlock}}

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
$PowerForgeDesktopBinaryDirectories = @()
try {
    $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
    foreach ($Library in $LibraryFileNames) {
        $ResolvedModuleAssembly = & $ResolvePowerForgeModuleAssembly -LibraryFileName $Library
        $ModuleAssemblyPath = $ResolvedModuleAssembly.Path
        $LibFolder = $ResolvedModuleAssembly.Folder
        $LibraryDirectory = $ResolvedModuleAssembly.Directory
        $LibraryName = [IO.Path]::GetFileNameWithoutExtension($ModuleAssemblyPath)
        $Class = "$LibraryName.Initialize"

        if ($PSEdition -eq 'Core') {
            $LoaderAssemblyPath = [IO.Path]::Combine($LibraryDirectory, '{{LoaderAssemblyName}}.dll')
            if (-not ('{{LoaderTypeName}}' -as [type])) {
                Add-Type -Path $LoaderAssemblyPath -ErrorAction Stop
            }

            $ModuleAssembly = [{{LoaderTypeName}}]::LoadModule($ModuleAssemblyPath, '{{ModuleName}}.' + $LibraryName)
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
            if ($LibraryDirectory -notin $PowerForgeDesktopBinaryDirectories) {
                $PowerForgeDesktopBinaryDirectories += $LibraryDirectory
            }
        }
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
    foreach ($PowerForgeDesktopBinaryDirectory in $PowerForgeDesktopBinaryDirectories) {
{{DesktopTypeAcceleratorBlock}}
    }
}
if ($PSEdition -ne 'Core' -and $null -ne $PowerForgeDesktopAssemblyResolverState) {
    $PowerForgeDesktopAssemblyResolverState.BootstrapActive = $false
    if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
        & $UnregisterPowerForgeDesktopAssemblyResolver
    }
}
