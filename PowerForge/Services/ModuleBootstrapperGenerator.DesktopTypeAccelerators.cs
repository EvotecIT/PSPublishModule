namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static string BuildDesktopTypeAcceleratorBlock(
        AssemblyTypeAcceleratorExportMode mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames,
        string libraryDirectoryExpression,
        IReadOnlyList<string>? ignoreLibrariesOnLoad = null)
    {
        var normalizedTypes = NormalizePowerShellStringArray(typeNames);
        var normalizedAssemblies = NormalizePowerShellStringArray(assemblyNames);
        var ignoredLibraryFileNames = NormalizeFileNameSet(ignoreLibrariesOnLoad)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(libraryDirectoryExpression))
            throw new ArgumentException("A Desktop library directory expression is required.", nameof(libraryDirectoryExpression));

        return $@"if ($PSEdition -ne 'Core') {{
    # Desktop loads module dependencies into the default AppDomain, so expose the same configured scripting types as Core.
    $RegisterPowerForgeDesktopAssemblyTypeAccelerators = {{
        param([Parameter(Mandatory = $true)][string] $LibraryDirectory)

        $Mode = '{mode}'
        $RequestedTypes = {BuildPowerShellArrayLiteral(normalizedTypes)}
        $RequestedAssemblies = {BuildPowerShellArrayLiteral(normalizedAssemblies)}
        $IgnoredLibraryFileNames = {BuildPowerShellArrayLiteral(ignoredLibraryFileNames)}

        if ([string]::IsNullOrWhiteSpace($LibraryDirectory) -or -not (Test-Path -LiteralPath $LibraryDirectory)) {{
            Write-Warning -Message 'Module library directory was not available. Desktop dependency type exposure is disabled.'
            return
        }}

        if ($Mode -eq 'AllowList' -and $RequestedTypes.Count -eq 0) {{
            Write-Warning -Message 'AllowList type accelerator mode was configured without type names. No Desktop dependency type accelerators will be registered.'
            return
        }}

        if (($Mode -eq 'Assembly' -or $Mode -eq 'Enums') -and $RequestedAssemblies.Count -eq 0) {{
            if ($RequestedTypes.Count -eq 0) {{
                Write-Warning -Message ""$Mode type accelerator mode was configured without assembly names or type names. No Desktop dependency type accelerators will be registered.""
                return
            }}

            Write-Warning -Message ""$Mode type accelerator mode was configured without assembly names. Only explicitly configured type names will be registered.""
        }}

        $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
        if ($null -eq $TypeAccelerators) {{
            Write-Warning -Message 'PowerShell type accelerator APIs are not available. Desktop dependency type exposure is disabled.'
            return
        }}

        $AddTypeAccelerator = $TypeAccelerators.GetMethod('Add', [type[]]@([string], [type]))
        $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
        if ($null -eq $AddTypeAccelerator -or $null -eq $GetTypeAccelerators) {{
            Write-Warning -Message 'PowerShell type accelerator APIs are incomplete. Desktop dependency type exposure is disabled.'
            return
        }}

        if ($null -eq $script:PowerForgeRegisteredAssemblyTypeAccelerators) {{
            $script:PowerForgeRegisteredAssemblyTypeAccelerators = @{{}}
        }}

        $ResolvedPowerForgeDesktopAssemblies = @{{}}
        $FailedPowerForgeDesktopAssemblies = @{{}}
        $LibraryDirectoryFullPath = [IO.Path]::GetFullPath($LibraryDirectory)
        $LibraryDirectoryPrefix = $LibraryDirectoryFullPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

        $TestPowerForgeDesktopIgnoredAssembly = {{
            param([Parameter(Mandatory = $true)] $Assembly)

            return $IgnoredLibraryFileNames -contains ($Assembly.GetName().Name + '.dll')
        }}

        $TestPowerForgeDesktopModuleAssembly = {{
            param([Parameter(Mandatory = $true)] $Assembly)

            if ($Assembly.IsDynamic) {{
                return $false
            }}

            try {{
                if ([string]::IsNullOrWhiteSpace($Assembly.Location)) {{
                    return $false
                }}

                $AssemblyFullPath = [IO.Path]::GetFullPath($Assembly.Location)
                return $AssemblyFullPath.StartsWith($LibraryDirectoryPrefix, [StringComparison]::OrdinalIgnoreCase)
            }} catch {{
                return $false
            }}
        }}

        $TestPowerForgeDesktopAssemblyContentMatch = {{
            param(
                [Parameter(Mandatory = $true)] $Assembly,
                [Parameter(Mandatory = $true)][string] $ExpectedPath
            )

            if ($Assembly.IsDynamic -or
                [string]::IsNullOrWhiteSpace($Assembly.Location) -or
                -not (Test-Path -LiteralPath $ExpectedPath)) {{
                return $false
            }}

            try {{
                $ExpectedName = [Reflection.AssemblyName]::GetAssemblyName($ExpectedPath)
                if ($Assembly.GetName().FullName -ne $ExpectedName.FullName) {{
                    return $false
                }}

                $LoadedPath = [IO.Path]::GetFullPath($Assembly.Location)
                $ExpectedFullPath = [IO.Path]::GetFullPath($ExpectedPath)
                if ([string]::Equals($LoadedPath, $ExpectedFullPath, [StringComparison]::OrdinalIgnoreCase)) {{
                    return $true
                }}

                $ComputePowerForgeDesktopAssemblyHash = {{
                    param([Parameter(Mandatory = $true)][string] $Path)

                    $Stream = $null
                    $Hasher = $null
                    try {{
                        $Stream = [IO.File]::OpenRead($Path)
                        $Hasher = [Security.Cryptography.SHA256]::Create()
                        return [Convert]::ToBase64String($Hasher.ComputeHash($Stream))
                    }} finally {{
                        if ($null -ne $Hasher) {{
                            $Hasher.Dispose()
                        }}
                        if ($null -ne $Stream) {{
                            $Stream.Dispose()
                        }}
                    }}
                }}

                $LoadedHash = & $ComputePowerForgeDesktopAssemblyHash -Path $LoadedPath
                $ExpectedHash = & $ComputePowerForgeDesktopAssemblyHash -Path $ExpectedFullPath
                if ($LoadedHash -eq $ExpectedHash) {{
                    return $true
                }}

                $LoadedProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($LoadedPath).ProductVersion
                $ExpectedProductVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExpectedFullPath).ProductVersion
                return -not [string]::IsNullOrWhiteSpace($LoadedProductVersion) -and
                    $LoadedProductVersion.Contains('+') -and
                    [string]::Equals($LoadedProductVersion, $ExpectedProductVersion, [StringComparison]::Ordinal)
            }} catch {{
                return $false
            }}
        }}

        $ImportPowerForgeDesktopAssembly = {{
            param([Parameter(Mandatory = $true)][string] $AssemblyName)

            $SimpleName = if ($AssemblyName.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {{
                [IO.Path]::GetFileNameWithoutExtension($AssemblyName)
            }} else {{
                $AssemblyName
            }}
            $AssemblyFileName = $SimpleName + '.dll'
            if ($IgnoredLibraryFileNames -contains $AssemblyFileName) {{
                $FailedPowerForgeDesktopAssemblies[$SimpleName] = $true
                return $null
            }}

            if ($ResolvedPowerForgeDesktopAssemblies.ContainsKey($SimpleName)) {{
                return $ResolvedPowerForgeDesktopAssemblies[$SimpleName]
            }}
            if ($FailedPowerForgeDesktopAssemblies.ContainsKey($SimpleName)) {{
                return $null
            }}

            $AssemblyPath = [IO.Path]::Combine($LibraryDirectory, $AssemblyFileName)
            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                if ($Assembly.GetName().Name -eq $SimpleName) {{
                    if ((& $TestPowerForgeDesktopModuleAssembly -Assembly $Assembly) -or
                        (& $TestPowerForgeDesktopAssemblyContentMatch -Assembly $Assembly -ExpectedPath $AssemblyPath)) {{
                        $ResolvedPowerForgeDesktopAssemblies[$SimpleName] = $Assembly
                        return $Assembly
                    }}
                }}
            }}

            if (Test-Path -LiteralPath $AssemblyPath) {{
                try {{
                    $LoadedAssembly = [System.Reflection.Assembly]::LoadFrom($AssemblyPath)
                    if ((& $TestPowerForgeDesktopModuleAssembly -Assembly $LoadedAssembly) -or
                        (& $TestPowerForgeDesktopAssemblyContentMatch -Assembly $LoadedAssembly -ExpectedPath $AssemblyPath)) {{
                        $ResolvedPowerForgeDesktopAssemblies[$SimpleName] = $LoadedAssembly
                        return $LoadedAssembly
                    }}

                    $FailedPowerForgeDesktopAssemblies[$SimpleName] = $true
                    Write-Warning -Message ""Desktop assembly '$SimpleName' resolved outside the module library directory. Skipping type accelerator exposure to avoid registering the wrong assembly version.""
                    return $null
                }} catch {{
                    $FailedPowerForgeDesktopAssemblies[$SimpleName] = $true
                    Write-Warning -Message ""Could not load Desktop assembly '$SimpleName' for type accelerator exposure: $($_.Exception.Message)""
                    return $null
                }}
            }}

            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                if ($Assembly.GetName().Name -eq $SimpleName) {{
                    $ResolvedPowerForgeDesktopAssemblies[$SimpleName] = $Assembly
                    return $Assembly
                }}
            }}

            $FailedPowerForgeDesktopAssemblies[$SimpleName] = $true
            return $null
        }}

        $FindPowerForgeDesktopType = {{
            param([Parameter(Mandatory = $true)][string] $TypeName)

            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                if ((& $TestPowerForgeDesktopIgnoredAssembly -Assembly $Assembly) -or
                    -not (& $TestPowerForgeDesktopModuleAssembly -Assembly $Assembly)) {{
                    continue
                }}

                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {{
                    return $Type
                }}
            }}

            foreach ($File in Get-ChildItem -LiteralPath $LibraryDirectory -Filter '*.dll' -File -ErrorAction SilentlyContinue) {{
                if ($IgnoredLibraryFileNames -contains $File.Name) {{
                    continue
                }}

                $Assembly = & $ImportPowerForgeDesktopAssembly -AssemblyName $File.BaseName
                if ($null -eq $Assembly) {{
                    continue
                }}

                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {{
                    return $Type
                }}
            }}

            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                if (& $TestPowerForgeDesktopIgnoredAssembly -Assembly $Assembly) {{
                    continue
                }}

                $AssemblyFileName = $Assembly.GetName().Name + '.dll'
                if (Test-Path -LiteralPath ([IO.Path]::Combine($LibraryDirectory, $AssemblyFileName))) {{
                    # A module-owned assembly with this identity exists but could not be selected above. Do not
                    # silently fall back to a globally loaded copy with a different location or version.
                    continue
                }}

                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {{
                    return $Type
                }}
            }}

            return $null
        }}

        $AddPowerForgeDesktopTypeAccelerator = {{
            param([Parameter(Mandatory = $true)][type] $Type)

            if ([string]::IsNullOrWhiteSpace($Type.FullName)) {{
                return
            }}

            $Name = $Type.FullName
            $Existing = $GetTypeAccelerators.GetValue($null)
            if ($Existing.ContainsKey($Name)) {{
                $ExistingType = $Existing[$Name]
                if ([object]::ReferenceEquals($ExistingType, $Type)) {{
                    return
                }}

                Write-Warning -Message ""Type accelerator '$Name' already exists from $($ExistingType.Assembly.GetName().FullName). Keeping it and skipping the Desktop type from $($Type.Assembly.GetName().FullName).""
                return
            }}

            try {{
                $AddTypeAccelerator.Invoke($null, @($Name, $Type)) | Out-Null
            }} catch {{
                Write-Warning -Message ""Type accelerator '$Name' could not be registered from $($Type.Assembly.GetName().Name): $($_.Exception.Message)""
                return
            }}

            $script:PowerForgeRegisteredAssemblyTypeAccelerators[$Name] = $Type
        }}

        if ($Mode -eq 'Assembly' -or $Mode -eq 'Enums') {{
            foreach ($AssemblyName in $RequestedAssemblies) {{
                $Assembly = & $ImportPowerForgeDesktopAssembly -AssemblyName $AssemblyName
                if ($null -eq $Assembly) {{
                    Write-Warning -Message ""Assembly '$AssemblyName' was not found in the module library directory. No Desktop type accelerators were registered for it.""
                    continue
                }}

                try {{
                    $ExportedTypes = @($Assembly.GetExportedTypes())
                }} catch {{
                    Write-Warning -Message ""Could not enumerate exported types from Desktop assembly '$AssemblyName' for type accelerator exposure: $($_.Exception.Message)""
                    continue
                }}

                foreach ($Type in $ExportedTypes) {{
                    if ($Mode -eq 'Enums' -and -not $Type.IsEnum) {{
                        continue
                    }}

                    & $AddPowerForgeDesktopTypeAccelerator -Type $Type
                }}
            }}
        }}

        foreach ($TypeName in $RequestedTypes) {{
            $Type = & $FindPowerForgeDesktopType -TypeName $TypeName
            if ($null -eq $Type) {{
                Write-Warning -Message ""Type '$TypeName' was not found in the module AppDomain. No Desktop type accelerator was registered.""
                continue
            }}

            & $AddPowerForgeDesktopTypeAccelerator -Type $Type
        }}

        if ($script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered -ne $true) {{
            $script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered = $true
            $RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators
            $PreviousPowerForgeOnRemove = $ExecutionContext.SessionState.Module.OnRemove
            $ExecutionContext.SessionState.Module.OnRemove = {{
                try {{
                    $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                    if ($null -eq $TypeAccelerators -or $null -eq $RegisteredPowerForgeTypeAccelerators) {{
                        return
                    }}

                    $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
                    $RemoveTypeAccelerator = $TypeAccelerators.GetMethod('Remove', [type[]]@([string]))
                    if ($null -eq $GetTypeAccelerators -or $null -eq $RemoveTypeAccelerator) {{
                        return
                    }}

                    $Existing = $GetTypeAccelerators.GetValue($null)
                    foreach ($Entry in @($RegisteredPowerForgeTypeAccelerators.GetEnumerator())) {{
                        if ($Existing.ContainsKey($Entry.Key) -and [object]::ReferenceEquals($Existing[$Entry.Key], $Entry.Value)) {{
                            $RemoveTypeAccelerator.Invoke($null, @($Entry.Key)) | Out-Null
                        }}
                    }}
                }} finally {{
                    if ($null -ne $PreviousPowerForgeOnRemove) {{
                        & $PreviousPowerForgeOnRemove @args
                    }}
                }}
            }}.GetNewClosure()
        }}
    }}

    try {{
        & $RegisterPowerForgeDesktopAssemblyTypeAccelerators -LibraryDirectory ({libraryDirectoryExpression})
    }} catch {{
        Write-Warning -Message ""Desktop type accelerator registration failed: $($_.Exception.Message)""
    }}
}}";
    }
}
