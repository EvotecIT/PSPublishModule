Function Get-PowerShellAssemblyMetadata {
    <#
    .SYNOPSIS
    Gets the cmdlets and aliases in a dotnet assembly.

    .PARAMETER Path
    The assembly to inspect.

    .EXAMPLE
    Get-PowerShellAssemblyMetadata -Path MyModule.dll

    .NOTES
    This requires the System.Reflection.MetadataLoadContext assembly to be
    loaded through Add-Type. WinPS (5.1) will also need to load its deps
        System.Memory
        System.Collections.Immutable
        System.Reflection.Metadata
        System.Runtime.CompilerServices.Unsafe

    https://www.nuget.org/packages/System.Reflection.MetadataLoadContext

    Copyright: (c) 2024, Jordan Borean (@jborean93) <jborean93@gmail.com>
    MIT License (see LICENSE or https://opensource.org/licenses/MIT)
    #>
    [CmdletBinding()]
    param (
        [Parameter(Mandatory)][string] $Path
    )
    Write-Text -Text "   [+] Loading assembly $Path" -Color Cyan
    # Get the path to System.Management.Automation assembly
    $smaAssembly = [System.Management.Automation.PSObject].Assembly
    $smaAssemblyPath = $smaAssembly.Location

    if (-not $smaAssemblyPath) {
        $smaAssemblyPath = $smaAssembly.CodeBase
        if ($smaAssemblyPath -like 'file://*') {
            $smaAssemblyPath = $smaAssemblyPath -replace 'file:///', ''
            $smaAssemblyPath = [System.Uri]::UnescapeDataString($smaAssemblyPath)
        } else {
            Write-Text -Text "[-] Could not determine the path to System.Management.Automation assembly." -Color Red
            return $false
        }
    }

    $assemblyDirectory = Split-Path -Path $Path
    $runtimeAssemblies = Get-ChildItem -Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) -Filter "*.dll"
    $assemblyFiles = Get-ChildItem -Path $assemblyDirectory -Filter "*.dll" -Recurse

    $resolverCandidates = @(
        $assemblyFiles.FullName
        $runtimeAssemblies.FullName
        $smaAssemblyPath
    )

    $uniquePaths = [System.Collections.Generic.Dictionary[string,string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $resolverCandidates) {
        if (-not $candidate) { continue }
        $name = [System.IO.Path]::GetFileNameWithoutExtension($candidate)
        if (-not $uniquePaths.ContainsKey($name)) {
            $uniquePaths[$name] = $candidate
        }
    }

    try {
        $resolver = [System.Reflection.PathAssemblyResolver]::new($uniquePaths.Values)
    } catch {
        Write-Text -Text "   [-] Can't create PathAssemblyResolver. Please ensure all dependencies are present. Error: $($_.Exception.Message)" -Color Red
        return $false
    }

    $context = $null
    try {
        $context = [System.Reflection.MetadataLoadContext]::new($resolver)

        # Load target assembly first
        $assembly = $context.LoadFromAssemblyPath($Path)
        Write-Verbose -Message "Loaded assembly $($assembly.FullName), $($assembly.Location) searching for cmdlets and aliases"

        # Resolve SMA inside the same MetadataLoadContext by name to avoid type identity mismatches
        $smaRef = ($assembly.GetReferencedAssemblies() | Where-Object { $_.Name -eq 'System.Management.Automation' } | Select-Object -First 1)
        if ($null -ne $smaRef) {
            $smaAssemblyInContext = $context.LoadFromAssemblyName($smaRef)
        } else {
            # Fallback to host SMA path if not referenced explicitly (unusual)
            $smaAssemblyInContext = $context.LoadFromAssemblyPath($smaAssemblyPath)
        }

        $cmdletTypeName = 'System.Management.Automation.Cmdlet'
        $pscmdletTypeName = 'System.Management.Automation.PSCmdlet'
        $cmdletAttributeName = 'System.Management.Automation.CmdletAttribute'
        $aliasAttributeName = 'System.Management.Automation.AliasAttribute'

        $cmdletsToExport = [System.Collections.Generic.List[string]]::new()
        $aliasesToExport = [System.Collections.Generic.List[string]]::new()

        try {
            $Types = $assembly.GetTypes()
        } catch {
            Write-Verbose -Message "Falling back to GetExportedTypes() due to: $($_.Exception.Message)"
            $Types = $assembly.GetExportedTypes()
        }

        foreach ($type in $Types) {
            # Robust cmdlet detection: prefer attribute FullName match to avoid identity issues
            $attr = $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq $cmdletAttributeName } | Select-Object -First 1

            if (-not $attr) {
                # Fallback: walk base types by FullName to detect Cmdlet/PSCmdlet inheritance
                $bt = $type.BaseType
                $isCmdlet = $false
                while ($bt) {
                    if ($bt.FullName -eq $cmdletTypeName -or $bt.FullName -eq $pscmdletTypeName) { $isCmdlet = $true; break }
                    $bt = $bt.BaseType
                }
                if (-not $isCmdlet) { continue }
            }

            # Extract Verb-Noun from attribute where available
            if ($attr) {
                $verb = $null; $noun = $null
                if ($attr.ConstructorArguments.Count -ge 2) {
                    $verb = [string]$attr.ConstructorArguments[0].Value
                    $noun = [string]$attr.ConstructorArguments[1].Value
                }
                if (-not $verb -or -not $noun) {
                    $verb = [string]($attr.NamedArguments | Where-Object { $_.MemberName -eq 'VerbName' } | Select-Object -ExpandProperty TypedValue -ErrorAction Ignore).Value
                    $noun = [string]($attr.NamedArguments | Where-Object { $_.MemberName -eq 'NounName' } | Select-Object -ExpandProperty TypedValue -ErrorAction Ignore).Value
                }
                if ($verb -and $noun) {
                    $cmdletsToExport.Add("$verb-$noun")
                }
            }

            # Aliases (if any)
            $aliasAttrs = $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq $aliasAttributeName }
            foreach ($aa in $aliasAttrs) {
                if ($aa.ConstructorArguments -and $aa.ConstructorArguments.Value) {
                    $aliasesToExport.AddRange([string[]]@($aa.ConstructorArguments.Value.Value))
                }
            }
        }

        [PSCustomObject]@{
            CmdletsToExport = ($cmdletsToExport | Select-Object -Unique)
            AliasesToExport = ($aliasesToExport | Select-Object -Unique)
        }
    } catch {
        if ($_.Exception.Message -like '*has already been loaded into this MetadataLoadContext*') {
            Write-Text -Text "   [-] Can't load assembly $Path. Error: $($_.Exception.Message)" -Color Yellow
            [PSCustomObject]@{
                CmdletsToExport = @()
                AliasesToExport = @()
            }
        } else {
            Write-Text -Text "   [-] Can't load assembly $Path. Error: $($_.Exception.Message)" -Color Red
            $false
        }
    } finally {
        if ($null -ne $context) {
            $context.Dispose()
        }
    }
}
