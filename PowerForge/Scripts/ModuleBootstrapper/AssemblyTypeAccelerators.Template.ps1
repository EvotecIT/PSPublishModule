        # Type accelerator registration relies on $ModuleAssembly and $LibFolder from this ALC loader scope.
$RegisterPowerForgeAssemblyTypeAccelerators = {
    param(
        [Parameter(Mandatory = $true)][System.Reflection.Assembly] $ModuleAssembly,
        [Parameter(Mandatory = $true)][string] $LibFolder
    )

    $Mode = '{{Mode}}'
    $RequestedTypes = {{RequestedTypes}}
    $RequestedAssemblies = {{RequestedAssemblies}}

    if ($null -eq $ModuleAssembly) {
        Write-Warning -Message 'Module assembly was not available. ALC dependency type exposure is disabled.'
        return
    }

    if ([string]::IsNullOrWhiteSpace($LibFolder)) {
        Write-Warning -Message 'Module library folder was not available. ALC dependency type exposure is disabled.'
        return
    }

    $PowerForgeAlcLibraryDirectory = $null
    if ([IO.Path]::IsPathRooted($LibFolder)) {
        $PowerForgeAlcLibraryDirectory = [IO.Path]::GetFullPath($LibFolder)
    } elseif ($LibFolder.Contains('..') -or $LibFolder.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        Write-Warning -Message "Module library folder '$LibFolder' must be a simple folder name or a rooted development binary directory. ALC dependency type exposure is disabled."
        return
    } else {
        $PowerForgeAlcLibraryDirectory = [IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder)
    }

    if ($Mode -eq 'AllowList' -and $RequestedTypes.Count -eq 0) {
        Write-Warning -Message 'AllowList type accelerator mode was configured without type names. No ALC dependency type accelerators will be registered.'
        return
    }

    if (($Mode -eq 'Assembly' -or $Mode -eq 'Enums') -and $RequestedAssemblies.Count -eq 0) {
        if ($RequestedTypes.Count -eq 0) {
            Write-Warning -Message "$Mode type accelerator mode was configured without assembly names or type names. No ALC dependency type accelerators will be registered."
            return
        }

        Write-Warning -Message "$Mode type accelerator mode was configured without assembly names. Only explicitly configured type names will be registered."
    }

    $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
    if ($null -eq $TypeAccelerators) {
        Write-Warning -Message 'PowerShell type accelerator APIs are not available. ALC dependency type exposure is disabled.'
        return
    }

    $AddTypeAccelerator = $TypeAccelerators.GetMethod('Add', [type[]]@([string], [type]))
    $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
    if ($null -eq $AddTypeAccelerator -or $null -eq $GetTypeAccelerators) {
        Write-Warning -Message 'PowerShell type accelerator APIs are incomplete. ALC dependency type exposure is disabled.'
        return
    }

    $ModuleAlc = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($ModuleAssembly)
    if ($null -eq $ModuleAlc) {
        Write-Warning -Message 'Unable to resolve the module AssemblyLoadContext. ALC dependency type exposure is disabled.'
        return
    }

    if ($null -eq $script:PowerForgeRegisteredAssemblyTypeAccelerators) {
        $script:PowerForgeRegisteredAssemblyTypeAccelerators = @{}
    }

    $ImportPowerForgeAlcAssembly = {
        param([Parameter(Mandatory = $true)][string] $AssemblyName)

        foreach ($Assembly in $ModuleAlc.Assemblies) {
            if ($Assembly.GetName().Name -eq $AssemblyName) {
                return $Assembly
            }
        }

        try {
            return $ModuleAlc.LoadFromAssemblyName([System.Reflection.AssemblyName]::new($AssemblyName))
        } catch {
            $AssemblyPath = [IO.Path]::Combine($PowerForgeAlcLibraryDirectory, $AssemblyName + '.dll')
            if (Test-Path -LiteralPath $AssemblyPath) {
                try {
                    $AssemblyNameObject = [System.Reflection.AssemblyName]::GetAssemblyName($AssemblyPath)
                    return $ModuleAlc.LoadFromAssemblyName($AssemblyNameObject)
                } catch {
                    Write-Warning -Message "Could not load ALC assembly '$AssemblyName' for type accelerator exposure: $($_.Exception.Message)"
                }
            }
        }

        return $null
    }

    $FindPowerForgeAlcType = {
        param([Parameter(Mandatory = $true)][string] $TypeName)

        foreach ($Assembly in $ModuleAlc.Assemblies) {
            $Type = $Assembly.GetType($TypeName, $false, $false)
            if ($null -ne $Type) {
                return $Type
            }
        }

        $LibDirectory = $PowerForgeAlcLibraryDirectory
        if (-not (Test-Path -LiteralPath $LibDirectory)) {
            return $null
        }

        foreach ($File in Get-ChildItem -LiteralPath $LibDirectory -File -ErrorAction SilentlyContinue | Where-Object Extension -IEQ '.dll') {
            try {
                $AssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($File.FullName)
                $Assembly = & $ImportPowerForgeAlcAssembly -AssemblyName $AssemblyName.Name
                if ($null -eq $Assembly) {
                    continue
                }

                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {
                    return $Type
                }
            } catch {
                continue
            }
        }

        return $null
    }

    $AddPowerForgeTypeAccelerator = {
        param([Parameter(Mandatory = $true)][type] $Type)

        if ([string]::IsNullOrWhiteSpace($Type.FullName)) {
            return
        }

        $Name = $Type.FullName
        $Existing = $GetTypeAccelerators.GetValue($null)
        if ($Existing.ContainsKey($Name)) {
            $ExistingType = $Existing[$Name]
            if ([object]::ReferenceEquals($ExistingType, $Type)) {
                return
            } else {
                $ExistingAssemblyName = $ExistingType.Assembly.GetName()
                $TypeAssemblyName = $Type.Assembly.GetName()
                $ExistingLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($ExistingType.Assembly)
                $TypeLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($Type.Assembly)
                if ([object]::ReferenceEquals($ExistingLoadContext, $TypeLoadContext) -and [object]::Equals($ExistingAssemblyName.FullName, $TypeAssemblyName.FullName)) {
                    Write-Verbose -Message "Type accelerator '$Name' already exists in the same AssemblyLoadContext from the same assembly identity. Keeping the existing accelerator and skipping the duplicate type from $($TypeAssemblyName.Name)."
                } else {
                    Write-Warning -Message "Type accelerator '$Name' already exists from $($ExistingAssemblyName.FullName). Keeping the existing accelerator and skipping the ALC type from $($TypeAssemblyName.FullName)."
                }
            }
            return
        }

        try {
            $AddTypeAccelerator.Invoke($null, @($Name, $Type)) | Out-Null
        } catch {
            Write-Warning -Message "Type accelerator '$Name' could not be registered from $($Type.Assembly.GetName().Name): $($_.Exception.Message)"
            return
        }

        $script:PowerForgeRegisteredAssemblyTypeAccelerators[$Name] = $Type
    }

    if ($Mode -eq 'Assembly' -or $Mode -eq 'Enums') {
        foreach ($AssemblyName in $RequestedAssemblies) {
            $Assembly = & $ImportPowerForgeAlcAssembly -AssemblyName $AssemblyName
            if ($null -eq $Assembly) {
                Write-Warning -Message "Assembly '$AssemblyName' was not found in the module AssemblyLoadContext. No type accelerators were registered for it."
                continue
            }

            try {
                $ExportedTypes = @($Assembly.GetExportedTypes())
            } catch {
                Write-Warning -Message "Could not enumerate exported types from assembly '$AssemblyName' for type accelerator exposure: $($_.Exception.Message)"
                continue
            }

            foreach ($Type in $ExportedTypes) {
                if ($Mode -eq 'Enums' -and -not $Type.IsEnum) {
                    continue
                }

                & $AddPowerForgeTypeAccelerator -Type $Type
            }
        }
    }

    foreach ($TypeName in $RequestedTypes) {
        $Type = & $FindPowerForgeAlcType -TypeName $TypeName
        if ($null -eq $Type) {
            Write-Warning -Message "Type '$TypeName' was not found in the module AssemblyLoadContext. No type accelerator was registered."
            continue
        }

        & $AddPowerForgeTypeAccelerator -Type $Type
    }

    if ($script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered -ne $true) {
        $script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered = $true
        $RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators
        $PreviousPowerForgeOnRemove = $ExecutionContext.SessionState.Module.OnRemove
        $ExecutionContext.SessionState.Module.OnRemove = {
            try {
                $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                if ($null -eq $TypeAccelerators -or $null -eq $RegisteredPowerForgeTypeAccelerators) {
                    return
                }

                $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
                $RemoveTypeAccelerator = $TypeAccelerators.GetMethod('Remove', [type[]]@([string]))
                if ($null -eq $GetTypeAccelerators -or $null -eq $RemoveTypeAccelerator) {
                    return
                }

                $Existing = $GetTypeAccelerators.GetValue($null)
                foreach ($Entry in @($RegisteredPowerForgeTypeAccelerators.GetEnumerator())) {
                    if ($Existing.ContainsKey($Entry.Key) -and [object]::ReferenceEquals($Existing[$Entry.Key], $Entry.Value)) {
                        $RemoveTypeAccelerator.Invoke($null, @($Entry.Key)) | Out-Null
                    }
                }
            } finally {
                if ($null -ne $PreviousPowerForgeOnRemove) {
                    & $PreviousPowerForgeOnRemove @args
                }
            }
        }.GetNewClosure()
    }
}

# Type accelerator exposure is PowerShell Core-only because it depends on AssemblyLoadContext.
try {
    & $RegisterPowerForgeAssemblyTypeAccelerators -ModuleAssembly $ModuleAssembly -LibFolder $LibFolder
} catch {
    Write-Warning -Message "ALC type accelerator registration failed: $($_.Exception.Message)"
}
