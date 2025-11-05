function Get-ModuleInformation {
    <#
    .SYNOPSIS
    Gets module manifest information from a project directory

    .DESCRIPTION
    Retrieves module manifest (.psd1) file information from the specified path.
    Validates that exactly one manifest file exists and returns the parsed information.

    .PARAMETER Path
    The path to the directory containing the module manifest file

    .EXAMPLE
    Get-ModuleInformation -Path "C:\MyModule"

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    Write-Output "Module: $($moduleInfo.ModuleName) Version: $($moduleInfo.ModuleVersion)"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    try {
        # Validate path exists
        if (-not (Test-Path -Path $Path -PathType Container)) {
            throw "Path '$Path' does not exist or is not a directory"
        }

        # Find primary module manifest
        $PrimaryModule = Get-ChildItem -Path $Path -Filter '*.psd1' -Recurse -ErrorAction SilentlyContinue -Depth 1

        if (-not $PrimaryModule) {
            throw "Path '$Path' doesn't contain PSD1 files"
        }

        if ($PrimaryModule.Count -ne 1) {
            $foundFiles = $PrimaryModule | ForEach-Object { $_.FullName } | Join-String -Separator ', '
            throw "More than one PSD1 file detected in '$Path': $foundFiles"
        }

        # Import and validate manifest
        Write-Verbose "Loading module manifest from: $($PrimaryModule.FullName)"
        $PSDInformation = Import-PowerShellDataFile -Path $PrimaryModule.FullName -ErrorAction Stop

        # Get module name
        $ModuleName = $PrimaryModule.BaseName

        # Return comprehensive module information
        return @{
            ModuleName        = $ModuleName
            ManifestPath      = $PrimaryModule.FullName
            ModuleVersion     = $PSDInformation.ModuleVersion
            RequiredModules   = $PSDInformation.RequiredModules
            RootModule        = $PSDInformation.RootModule
            PowerShellVersion = $PSDInformation.PowerShellVersion
            ManifestData      = $PSDInformation
            ProjectPath       = $Path
        }
    } catch {
        Write-Error "Failed to get module information from '$Path': $($_.Exception.Message)"
        throw
    }
}
