{{BinaryAssemblyResolverBlock}}

{{ModuleLoaderImportBlock}}
{{SharedAssembliesBlock}}{{DesktopAssemblyResolverBlock}}{{RuntimeHandlerBlock}}if ($PSEdition -ne 'Core') {
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
    $PowerForgeResolvedBinaryModules = [Collections.Generic.List[object]]::new()
    $PowerForgeResolvedBinaryModulePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($Library in $LibraryFileNames) {
        try {
            $ResolvedModuleAssembly = & $ResolvePowerForgeModuleAssembly -LibraryFileName $Library
            $ResolvedModuleAssemblyPath = [IO.Path]::GetFullPath($ResolvedModuleAssembly.Path)
            if ($PowerForgeResolvedBinaryModulePaths.Add($ResolvedModuleAssemblyPath)) {
                $PowerForgeResolvedBinaryModules.Add([pscustomobject]@{
                    Library = $Library
                    Assembly = $ResolvedModuleAssembly
                })
            }
        } catch {
            if ($ErrorActionPreference -eq 'Stop') {
                if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                    & $UnregisterPowerForgeDesktopAssemblyResolver
                }
                throw
            } else {
                Write-Warning -Message "Resolving module $Library failed. Fix errors before continuing. Error: $($_.Exception.Message)"
            }
        }
    }
    $PowerForgeCoreModuleAssemblyPaths = [string[]]@($PowerForgeResolvedBinaryModules.Assembly.Path)

    for ($LibraryIndex = 0; $LibraryIndex -lt $PowerForgeResolvedBinaryModules.Count; $LibraryIndex++) {
        $Library = $PowerForgeResolvedBinaryModules[$LibraryIndex].Library
        $ResolvedModuleAssembly = $PowerForgeResolvedBinaryModules[$LibraryIndex].Assembly
        $ModuleAssemblyPath = $ResolvedModuleAssembly.Path
        $LibraryDirectory = $ResolvedModuleAssembly.Directory
        $LibFolder = $LibraryDirectory
        $LibraryName = [IO.Path]::GetFileNameWithoutExtension($ModuleAssemblyPath)
        $Class = "$LibraryName.Initialize"

        try {
            if ($PSEdition -eq 'Core') {
                $ModuleAssembly = [PowerForge.ModuleLoader.V1.ModuleAssemblyLoadContext]::LoadModuleFromGroup(
                    $PowerForgeCoreModuleAssemblyPaths,
                    $ModuleAssemblyPath,
                    '{{ModuleName}}',
                    [string[]]{{SharedAssemblyNames}})
                # On a repeat import (Import-Module -Force, or after Remove-Module) PowerShell returns its cached module
                # for this assembly without importing the commands here. Drop the cached copy so they import again.
                foreach ($PowerForgeStaleInnerModule in @(Get-Module -All | Where-Object { [object]::ReferenceEquals($_.ImplementingAssembly, $ModuleAssembly) })) {
                    Remove-Module -ModuleInfo $PowerForgeStaleInnerModule -Force -ErrorAction SilentlyContinue
                }
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
}
