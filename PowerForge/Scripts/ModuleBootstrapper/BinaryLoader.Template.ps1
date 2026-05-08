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
    return
}

if ($PSEdition -eq 'Core') {
    $LibFolder = $Framework
} else {
    $LibFolder = $FrameworkNet
}

{{RuntimeHandlerBlock}}try {
    $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core

    if (-not ($Class -as [type])) {
        & $ImportModule ([IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder, $Library)) -ErrorAction Stop
    } else {
        $Type = "$Class" -as [Type]
        & $importModule -Force -Assembly ($Type.Assembly)
    }
} catch {
    if ($ErrorActionPreference -eq 'Stop') {
        throw
    } else {
        Write-Warning -Message "Importing module $Library failed. Fix errors before continuing. Error: $($_.Exception.Message)"
    }
}

# Dot source all libraries by loading external file
$LibrariesScript = [IO.Path]::Combine($PSScriptRoot, '{{ModuleName}}.Libraries.ps1')
if (Test-Path -LiteralPath $LibrariesScript) {
    . $LibrariesScript
}
