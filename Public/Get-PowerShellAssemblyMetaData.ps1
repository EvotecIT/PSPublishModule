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
    Write-Text -Text "[-] Loading assembly $Path" -Color Cyan
    try {
        $assemblyDirectory = Split-Path -Path $Path
        $runtimeAssemblies = Get-ChildItem -Path ([System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) -Filter "*.dll"
        $assemblyFiles = Get-ChildItem -Path $assemblyDirectory -Filter "*.dll"

        $resolver = [System.Reflection.PathAssemblyResolver]::new(
            [string[]]@(
                $runtimeAssemblies.FullName
                $assemblyFiles.FullName
            )
        )
    } catch {
        Write-Text -Text "[-] Can't create PathAssemblyResolver. Please ensure all dependencies are present. Error: $($_.Exception.Message)" -Color Red
        return $false
    }
    try {
        $context = [System.Reflection.MetadataLoadContext]::new($resolver)
    } catch {
        Write-Text -Text "[-] Can't create MetadataLoadContext for file $Path. Skipping file, as most likely non-powershell binary. Error: $($_.Exception.Message)" -Color DarkYellow
        return
    }
    try {
        $smaAssembly = $context.LoadFromAssemblyPath([PSObject].Assembly.Location)
        $cmdletType = $smaAssembly.GetType('System.Management.Automation.Cmdlet')
        $cmdletAttribute = $smaAssembly.GetType('System.Management.Automation.CmdletAttribute')
        $aliasAttribute = $smaAssembly.GetType('System.Management.Automation.AliasAttribute')


        $assembly = $context.LoadFromAssemblyPath($Path)

        Write-Verbose -Message "Loaded assembly $($assembly.FullName), $($assembly.Location) searching for cmdlets and aliases"

        $cmdletsToExport = [System.Collections.Generic.List[string]]::new()
        $aliasesToExport = [System.Collections.Generic.List[string]]::new()
        $Types = $assembly.GetTypes()

        $Types | Where-Object {
            $_.IsSubclassOf($cmdletType)
        } | ForEach-Object -Process {
            $cmdletInfo = $_.CustomAttributes | Where-Object { $_.AttributeType -eq $cmdletAttribute }
            if (-not $cmdletInfo) { return }

            $name = "$($cmdletInfo.ConstructorArguments[0].Value)-$($cmdletInfo.ConstructorArguments[1].Value)"
            $cmdletsToExport.Add($name)

            $aliases = $_.CustomAttributes | Where-Object { $_.AttributeType -eq $aliasAttribute }
            if (-not $aliases -or -not $aliases.ConstructorArguments.Value) { return }
            $aliasesToExport.AddRange([string[]]@($aliases.ConstructorArguments.Value.Value))
        }

        [PSCustomObject]@{
            CmdletsToExport = $cmdletsToExport
            AliasesToExport = $aliasesToExport
        }
    } catch {
        Write-Text -Text "[-] Can't load assembly $Path. Error: $($_.Exception.Message)" -Color Red
        $context.Dispose()
        return $false
    } finally {
        $context.Dispose()
    }
}