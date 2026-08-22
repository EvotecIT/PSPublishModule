$UnregisterPowerForgeDesktopAssemblyResolver = $null
$PowerForgeDesktopAssemblyRoots = @()
if ($PSEdition -ne 'Core') {
    if ($null -ne $ResolvePowerForgeModuleAssembly -and $LibraryFileNames.Count -gt 0) {
        foreach ($PowerForgeDesktopLibraryFileName in $LibraryFileNames) {
            $PowerForgeDesktopResolvedModule = & $ResolvePowerForgeModuleAssembly -LibraryFileName $PowerForgeDesktopLibraryFileName
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
    $PowerForgeDesktopAssemblyResolverState = [pscustomobject]@{
        BootstrapActive = $true
        Registered      = $false
    }

    $PowerForgeDesktopAssemblyResolver = [System.ResolveEventHandler] {
        param([object] $Sender, [ResolveEventArgs] $EventArgs)

        try {
            if ($null -eq $EventArgs) {
                return $null
            }

            # AssemblyResolve is AppDomain-wide on Windows PowerShell. During the
            # bounded preload/import window the CLR can omit RequestingAssembly
            # while reconciling netstandard dependency versions. Outside that
            # window, only service requests attributable to this module's private
            # Lib folder.
            if ($null -eq $EventArgs.RequestingAssembly -or
                [string]::IsNullOrWhiteSpace($EventArgs.RequestingAssembly.Location)) {
                if (-not $PowerForgeDesktopAssemblyResolverState.BootstrapActive) {
                    return $null
                }
            } else {
                $PowerForgeRequestingAssemblyPath = [IO.Path]::GetFullPath($EventArgs.RequestingAssembly.Location)
                $PowerForgeRequestFromModuleRoot = @($PowerForgeDesktopAssemblyRootPrefixes | Where-Object {
                    $PowerForgeRequestingAssemblyPath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase)
                }).Count -gt 0
                if (-not $PowerForgeRequestFromModuleRoot) {
                    return $null
                }
            }

            $PowerForgeRequestedAssemblyName = [Reflection.AssemblyName]::new($EventArgs.Name).Name
            if ([string]::IsNullOrWhiteSpace($PowerForgeRequestedAssemblyName)) {
                return $null
            }

            # AssemblyName.Name is expected to be a simple name. Enforce that
            # contract before using it as a path segment, then verify the
            # canonical candidate remains beneath the private assembly root.
            if ($PowerForgeRequestedAssemblyName -ne [IO.Path]::GetFileName($PowerForgeRequestedAssemblyName) -or
                $PowerForgeRequestedAssemblyName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
                return $null
            }

            foreach ($PowerForgeDesktopAssemblyRoot in $PowerForgeDesktopAssemblyRoots) {
                $PowerForgeAssemblyCandidate = [IO.Path]::GetFullPath(
                    [IO.Path]::Combine($PowerForgeDesktopAssemblyRoot, $PowerForgeRequestedAssemblyName + '.dll'))
                $PowerForgeDesktopAssemblyRootPrefix = [IO.Path]::GetFullPath($PowerForgeDesktopAssemblyRoot)
                if (-not $PowerForgeDesktopAssemblyRootPrefix.EndsWith([IO.Path]::DirectorySeparatorChar.ToString(), [StringComparison]::Ordinal)) {
                    $PowerForgeDesktopAssemblyRootPrefix += [IO.Path]::DirectorySeparatorChar
                }
                if (-not $PowerForgeAssemblyCandidate.StartsWith($PowerForgeDesktopAssemblyRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                if ([IO.File]::Exists($PowerForgeAssemblyCandidate)) {
                    return [Reflection.Assembly]::LoadFrom($PowerForgeAssemblyCandidate)
                }
            }

            return $null
        } catch {
            return $null
        }
    }.GetNewClosure()

    [AppDomain]::CurrentDomain.add_AssemblyResolve($PowerForgeDesktopAssemblyResolver)
    $PowerForgeDesktopAssemblyResolverState.Registered = $true
    $PowerForgeResolverForRemoval = $PowerForgeDesktopAssemblyResolver
    $UnregisterPowerForgeDesktopAssemblyResolver = {
        if ($PowerForgeDesktopAssemblyResolverState.Registered) {
            [AppDomain]::CurrentDomain.remove_AssemblyResolve($PowerForgeResolverForRemoval)
            $PowerForgeDesktopAssemblyResolverState.Registered = $false
        }
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
