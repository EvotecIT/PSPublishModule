namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static string BuildDesktopTypeAcceleratorBlock(
        AssemblyTypeAcceleratorExportMode mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames,
        string libraryDirectoryExpression)
    {
        var normalizedTypes = NormalizePowerShellStringArray(typeNames);
        var normalizedAssemblies = NormalizePowerShellStringArray(assemblyNames);
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

        $ImportPowerForgeDesktopAssembly = {{
            param([Parameter(Mandatory = $true)][string] $AssemblyName)

            $SimpleName = [IO.Path]::GetFileNameWithoutExtension($AssemblyName)
            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                if ($Assembly.GetName().Name -eq $SimpleName) {{
                    return $Assembly
                }}
            }}

            $AssemblyPath = [IO.Path]::Combine($LibraryDirectory, $SimpleName + '.dll')
            if (-not (Test-Path -LiteralPath $AssemblyPath)) {{
                return $null
            }}

            try {{
                return [System.Reflection.Assembly]::LoadFrom($AssemblyPath)
            }} catch {{
                Write-Warning -Message ""Could not load Desktop assembly '$SimpleName' for type accelerator exposure: $($_.Exception.Message)""
                return $null
            }}
        }}

        $FindPowerForgeDesktopType = {{
            param([Parameter(Mandatory = $true)][string] $TypeName)

            foreach ($Assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {{
                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {{
                    return $Type
                }}
            }}

            foreach ($File in Get-ChildItem -LiteralPath $LibraryDirectory -Filter '*.dll' -File -ErrorAction SilentlyContinue) {{
                $Assembly = & $ImportPowerForgeDesktopAssembly -AssemblyName $File.BaseName
                if ($null -eq $Assembly) {{
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
