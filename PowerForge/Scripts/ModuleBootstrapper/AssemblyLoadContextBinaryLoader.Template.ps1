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
    $PowerForgeResolvedBinaryModules = @(
        foreach ($Library in $LibraryFileNames) {
            [pscustomobject]@{
                Library = $Library
                Assembly = & $ResolvePowerForgeModuleAssembly -LibraryFileName $Library
            }
        }
    )
    $PowerForgeCoreModuleAssemblies = @()
    if ($PSEdition -eq 'Core' -and $PowerForgeResolvedBinaryModules.Count -gt 0) {
        $LoaderAssemblyPath = [IO.Path]::Combine($PowerForgeResolvedBinaryModules[0].Assembly.Directory, '{{LoaderAssemblyName}}.dll')
        if (-not ('{{LoaderTypeName}}' -as [type])) {
            Add-Type -Path $LoaderAssemblyPath -ErrorAction Stop
        }
        [array] $PowerForgeCoreModuleAssemblies = [{{LoaderTypeName}}]::LoadModules(
            [string[]]@($PowerForgeResolvedBinaryModules.Assembly.Path),
            '{{ModuleName}}')
    }

    for ($LibraryIndex = 0; $LibraryIndex -lt $PowerForgeResolvedBinaryModules.Count; $LibraryIndex++) {
        $Library = $PowerForgeResolvedBinaryModules[$LibraryIndex].Library
        $ResolvedModuleAssembly = $PowerForgeResolvedBinaryModules[$LibraryIndex].Assembly
        $ModuleAssemblyPath = $ResolvedModuleAssembly.Path
        $LibFolder = $ResolvedModuleAssembly.Folder
        $LibraryDirectory = $ResolvedModuleAssembly.Directory
        $LibraryName = [IO.Path]::GetFileNameWithoutExtension($ModuleAssemblyPath)
        $Class = "$LibraryName.Initialize"

        if ($PSEdition -eq 'Core') {
            $ModuleAssembly = $PowerForgeCoreModuleAssemblies[$LibraryIndex]
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
