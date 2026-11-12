$UnregisterPowerForgeDesktopAssemblyResolver = $null
$PowerForgeDesktopAssemblyRoots = @()
if ($PSEdition -ne 'Core') {
    if ($null -ne $ResolvePowerForgeModuleAssembly -and $LibraryFileNames.Count -gt 0) {
        foreach ($PowerForgeDesktopLibraryFileName in $LibraryFileNames) {
            try {
                $PowerForgeDesktopResolvedModule = & $ResolvePowerForgeModuleAssembly -LibraryFileName $PowerForgeDesktopLibraryFileName
            } catch {
                Write-Verbose "Skipping Desktop resolver root for '$PowerForgeDesktopLibraryFileName'. $($_.Exception.Message)"
                continue
            }
            if ($PowerForgeDesktopResolvedModule.Directory -notin $PowerForgeDesktopAssemblyRoots) {
                $PowerForgeDesktopAssemblyRoots += $PowerForgeDesktopResolvedModule.Directory
            }
        }
    } elseif ($LibFolder -or $Root) {
        $PowerForgeDesktopAssemblyRoots += if ($LibFolder) {
            [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder))
        } else {
            [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, 'Lib'))
        }
    }
}
if ($PSEdition -ne 'Core' -and $PowerForgeDesktopAssemblyRoots.Count -gt 0) {
    $PowerForgeDesktopAssemblyRootPrefixes = @($PowerForgeDesktopAssemblyRoots | ForEach-Object {
        $PowerForgeDesktopAssemblyRootPrefix = [IO.Path]::GetFullPath($_)
        if (-not $PowerForgeDesktopAssemblyRootPrefix.EndsWith([IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
            $PowerForgeDesktopAssemblyRootPrefix += [IO.Path]::DirectorySeparatorChar
        }
        $PowerForgeDesktopAssemblyRootPrefix
    })
    if ($PowerForgeDesktopAssemblyRootPrefixes.Count -eq 0) {
        $PowerForgeDesktopAssemblyRoots = @()
    }
    # AssemblyResolve also runs on worker threads that have no runspace, so the handler is compiled
    # code from the PowerForge loader and is limited to this module's own binary folders.
    $PowerForgeDesktopAssemblyResolverState = [PowerForge.ModuleLoader.V1.DesktopAssemblyResolver]::new([string[]]$PowerForgeDesktopAssemblyRoots, [string[]]{{SharedAssemblyNames}})
    $PowerForgeDesktopAssemblyResolver = $PowerForgeDesktopAssemblyResolverState.Handler
    $PowerForgeDesktopAssemblyResolverState.Register()
    $UnregisterPowerForgeDesktopAssemblyResolver = {
        $PowerForgeDesktopAssemblyResolverState.Unregister()
    }.GetNewClosure()

    $PowerForgePreviousOnRemove = $ExecutionContext.SessionState.Module.OnRemove
    $ExecutionContext.SessionState.Module.OnRemove = {
        try {
            if ($null -ne $UnregisterPowerForgeDesktopAssemblyResolver) {
                & $UnregisterPowerForgeDesktopAssemblyResolver
            }
        } finally {
            if ($null -ne $PowerForgePreviousOnRemove) {
                & $PowerForgePreviousOnRemove @args
            }
        }
    }.GetNewClosure()
}
