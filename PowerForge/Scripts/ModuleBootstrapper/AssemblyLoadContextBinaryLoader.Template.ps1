# Get library name, from the PSM1 file name
$LibraryName = '{{LibraryName}}'
$Library = "$LibraryName.dll"
$Class = "$LibraryName.Initialize"

$LibRoot = [IO.Path]::Combine($PSScriptRoot, 'Lib')
$AssemblyFolders = Get-ChildItem -LiteralPath $LibRoot -Directory -ErrorAction SilentlyContinue

$Default = $false
$Core = $false
$Standard = $false
foreach ($A in $AssemblyFolders.Name) {
    if ($A -eq 'Default') {
        $Default = $true
    } elseif ($A -eq 'Core') {
        $Core = $true
    } elseif ($A -eq 'Standard') {
        $Standard = $true
    }
}
if ($Standard -and $Core -and $Default) {
    $FrameworkNet = 'Default'
    $Framework = 'Standard'
} elseif ($Standard -and $Core) {
    $Framework = 'Standard'
    $FrameworkNet = 'Standard'
} elseif ($Core -and $Default) {
    $Framework = 'Core'
    $FrameworkNet = 'Default'
} elseif ($Standard -and $Default) {
    $Framework = 'Standard'
    $FrameworkNet = 'Default'
} elseif ($Standard) {
    $Framework = 'Standard'
    $FrameworkNet = 'Standard'
} elseif ($Core) {
    $Framework = 'Core'
    $FrameworkNet = ''
} elseif ($Default) {
    $Framework = 'Default'
    $FrameworkNet = 'Default'
} else {
    Write-Error -Message 'No assemblies found'
    return
}

if ($PSEdition -eq 'Core') {
    $LibFolder = $Framework
} else {
    $LibFolder = $FrameworkNet
}

{{RuntimeHandlerBlock}}try {
    $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
    $ModuleAssemblyPath = [IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder, $Library)

    if ($PSEdition -eq 'Core') {
        $LoaderAssemblyPath = [IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder, '{{LoaderAssemblyName}}.dll')
        if (-not ('{{LoaderTypeName}}' -as [type])) {
            Add-Type -Path $LoaderAssemblyPath -ErrorAction Stop
        }

        $ModuleAssembly = [{{LoaderTypeName}}]::LoadModule($ModuleAssemblyPath, '{{ModuleName}}')
        $InnerModule = & $ImportModule -Assembly $ModuleAssembly -Force -PassThru -ErrorAction Stop

        if ($InnerModule) {
            # Import-Module -Assembly loads the inner binary module into its own module object. PowerShell has no
            # public API to copy those exported cmdlets back to the script-module wrapper, so this uses the same
            # private PSModuleInfo hook used by community ALC loaders. This runs on first load and reloads so the
            # outer script module always re-exports cmdlets from the ALC-loaded binary module.
            $AddExportedCmdlet = [System.Management.Automation.PSModuleInfo].GetMethod(
                'AddExportedCmdlet',
                [System.Reflection.BindingFlags]'Instance, NonPublic'
            )
            if ($null -ne $AddExportedCmdlet) {
                foreach ($Cmd in $InnerModule.ExportedCmdlets.Values) {
                    $AddExportedCmdlet.Invoke($ExecutionContext.SessionState.Module, @(, $Cmd)) | Out-Null
                }
                $AddExportedAlias = [System.Management.Automation.PSModuleInfo].GetMethod(
                    'AddExportedAlias',
                    [System.Reflection.BindingFlags]'Instance, NonPublic'
                )
                if ($null -ne $AddExportedAlias) {
                    foreach ($Alias in $InnerModule.ExportedAliases.Values) {
                        $AliasTarget = if ([string]::IsNullOrWhiteSpace($Alias.Definition)) { $Alias.ResolvedCommandName } else { $Alias.Definition }
                        try {
                            # The alias must exist in this module scope before the private export table can reference it.
                            Set-Alias -Name $Alias.Name -Value $AliasTarget -Scope Local -Force -ErrorAction Stop
                            $ExportedAlias = $ExecutionContext.SessionState.InvokeCommand.GetCommand($Alias.Name, [System.Management.Automation.CommandTypes]::Alias)
                            if ($null -ne $ExportedAlias) {
                                $AddExportedAlias.Invoke($ExecutionContext.SessionState.Module, @(, $ExportedAlias)) | Out-Null
                            } else {
                                Write-Warning -Message "Alias '$($Alias.Name)' from $LibraryName was created but could not be resolved for export."
                            }
                        } catch {
                            Write-Warning -Message "Alias '$($Alias.Name)' from $LibraryName could not be re-exported: $($_.Exception.Message)"
                        }
                    }
                } else {
                    Write-Warning -Message "AddExportedAlias is not available on this PowerShell version. Aliases from $LibraryName will not be re-exported to the module scope."
                }
            } else {
                Write-Warning -Message "AddExportedCmdlet is not available on this PowerShell version. Falling back to direct Import-Module; cmdlets from $LibraryName will load from the default context."
                & $ImportModule $ModuleAssemblyPath -ErrorAction Stop
            }
        }
    } elseif (-not ($Class -as [type])) {
        & $ImportModule $ModuleAssemblyPath -ErrorAction Stop
    } else {
        $Type = "$Class" -as [Type]
        & $ImportModule -Force -Assembly ($Type.Assembly)
    }
} catch {
    if ($ErrorActionPreference -eq 'Stop') {
        throw
    } else {
        Write-Warning -Message "Importing module $Library failed. Fix errors before continuing. Error: $($_.Exception.Message)"
    }
}

if ($PSEdition -ne 'Core') {
    # Core loads dependencies through the module-scoped AssemblyLoadContext above. Dot-sourcing the libraries script
    # there would load dependency DLLs into the default context and undo the isolation this template exists to provide.
    $LibrariesScript = [IO.Path]::Combine($PSScriptRoot, '{{ModuleName}}.Libraries.ps1')
    if (Test-Path -LiteralPath $LibrariesScript) {
        . $LibrariesScript
    }
}
