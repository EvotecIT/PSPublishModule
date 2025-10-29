function Initialize-PortableModule {
    <#
    .SYNOPSIS
    Downloads and/or imports a module and its dependencies as a portable set.

    .DESCRIPTION
    Assists in preparing a portable environment for a module by either downloading it (plus dependencies)
    to a specified path, importing those modules from disk, or both. Generates a convenience script that
    imports all discovered module manifests when -Download is used.

    .PARAMETER Name
    Name of the module to download/import. Alias: ModuleName.

    .PARAMETER Path
    Filesystem path where modules are saved or imported from. Defaults to the current script root.

    .PARAMETER Download
    Save the module and its dependencies to the specified path.

    .PARAMETER Import
    Import the module and its dependencies from the specified path.

    .EXAMPLE
    Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Download
    Saves the module and its dependencies into C:\Portable.

    .EXAMPLE
    Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Import
    Imports the module and its dependencies from C:\Portable.

    .EXAMPLE
    Initialize-PortableModule -Name 'EFAdminManager' -Path 'C:\Portable' -Download -Import
    Saves and then imports the module and dependencies, and creates a helper script.
    #>
    [CmdletBinding()]
    param(
        [alias('ModuleName')][string] $Name,
        [string] $Path = $PSScriptRoot,
        [switch] $Download,
        [switch] $Import
    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    if (-not $Name) {
        Write-Warning "Initialize-ModulePortable - Module name not given. Terminating."
        return
    }
    if (-not $Download -and -not $Import) {
        Write-Warning "Initialize-ModulePortable - Please choose Download/Import switch. Terminating."
        return
    }
    if ($Download) {
        try {
            if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
                $null = New-Item -ItemType Directory -Path $Path -Force
            }
            Save-Module -Name $Name -LiteralPath $Path -WarningVariable WarningData -WarningAction SilentlyContinue -ErrorAction Stop
        } catch {
            $ErrorMessage = $_.Exception.Message
            if ($WarningData) {
                Write-Warning "Initialize-ModulePortable - $WarningData"
            }
            Write-Warning "Initialize-ModulePortable - Error $ErrorMessage"
            return
        }
    }
    if ($Download -or $Import) {
        [Array] $Modules = Get-RequiredModule -Path $Path -Name $Name | Where-Object { $null -ne $_ }
        if ($null -ne $Modules) {
            [array]::Reverse($Modules)
        }
        $CleanedModules = [System.Collections.Generic.List[string]]::new()
        foreach ($_ in $Modules) {
            if ($CleanedModules -notcontains $_) {
                $CleanedModules.Add($_)
            }
        }
        $CleanedModules.Add($Name)
        $Items = foreach ($_ in $CleanedModules) {
            Get-ChildItem -LiteralPath "$Path\$_" -Filter '*.psd1' -Recurse -ErrorAction SilentlyContinue -Depth 1
        }
        [Array] $PSD1Files = $Items.FullName
    }
    if ($Download) {
        $ListFiles = foreach ($PSD1 in $PSD1Files) {
            $PSD1.Replace("$Path", '$PSScriptRoot')
        }
        # Build File
        $Content = @(
            '$Modules = @('
            foreach ($_ in $ListFiles) {
                "   `"$_`""
            }
            ')'
            "foreach (`$_ in `$Modules) {"
            "   Import-Module `$_ -Verbose:`$false -Force"
            "}"
        )
        $Content | Set-Content -Path $Path\$Name.ps1 -Force -Encoding $Encoding
    }
    if ($Import) {
        $ListFiles = foreach ($PSD1 in $PSD1Files) {
            $PSD1
        }
        foreach ($_ in $ListFiles) {
            Import-Module $_ -Verbose:$false -Force
        }
    }
}
