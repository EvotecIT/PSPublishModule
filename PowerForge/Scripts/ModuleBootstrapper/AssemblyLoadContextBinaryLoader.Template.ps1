# Get library name, from the PSM1 file name
$LibraryName = '{{LibraryName}}'
$Library = "$LibraryName.dll"
$Class = "$LibraryName.Initialize"

$AssemblyFolders = Get-ChildItem -Path $PSScriptRoot\Lib -Directory -ErrorAction SilentlyContinue

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
    $Framework = ''
    $FrameworkNet = 'Default'
} else {
    Write-Error -Message 'No assemblies found'
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
        $IsReload = $true
        if (-not ('{{LoaderTypeName}}' -as [type])) {
            $IsReload = $false
            Add-Type -Path $LoaderAssemblyPath -ErrorAction Stop
        }

        $ModuleAssembly = [{{LoaderTypeName}}]::LoadModule($ModuleAssemblyPath, '{{ModuleName}}')
        $InnerModule = & $ImportModule -Assembly $ModuleAssembly -Force -PassThru:$IsReload -ErrorAction Stop

        if ($InnerModule) {
            $AddExportedCmdlet = [System.Management.Automation.PSModuleInfo].GetMethod(
                'AddExportedCmdlet',
                [System.Reflection.BindingFlags]'Instance, NonPublic'
            )
            foreach ($Cmd in $InnerModule.ExportedCmdlets.Values) {
                $AddExportedCmdlet.Invoke($ExecutionContext.SessionState.Module, @(, $Cmd)) | Out-Null
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
