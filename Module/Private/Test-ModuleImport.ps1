function Test-ModuleImport {
    <#
    .SYNOPSIS
    Tests module import with detailed error reporting

    .DESCRIPTION
    Attempts to import a module and provides comprehensive error reporting if the import fails.
    Can import from a manifest file path or by module name.

    .PARAMETER ModuleInformation
    Module information object returned by Get-ModuleInformation (optional if ModuleName or Path is provided)

    .PARAMETER ModuleName
    Name of the module to import (alternative to ModuleInformation)

    .PARAMETER Path
    Path to the module manifest file (alternative to ModuleInformation/ModuleName)

    .PARAMETER Force
    Force reimport of the module

    .PARAMETER ShowInformation
    Display module information after successful import

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    Test-ModuleImport -ModuleInformation $moduleInfo

    .EXAMPLE
    Test-ModuleImport -Path "C:\MyModule\MyModule.psd1"

    .EXAMPLE
    Test-ModuleImport -ModuleName "MyModule" -ShowInformation
    #>
    [CmdletBinding()]
    param(
        [Parameter(ParameterSetName = 'ModuleInfo')]
        [hashtable]$ModuleInformation,

        [Parameter(ParameterSetName = 'ModuleName', Mandatory)]
        [string]$ModuleName,

        [Parameter(ParameterSetName = 'Path', Mandatory)]
        [string]$Path,

        [Parameter()]
        [switch]$Force,

        [Parameter()]
        [switch]$ShowInformation
    )

    try {
        $ImportPath = $null
        $DisplayName = $null

        # Determine import path and display name based on parameter set
        switch ($PSCmdlet.ParameterSetName) {
            'ModuleInfo' {
                if (-not $ModuleInformation) {
                    throw "ModuleInformation parameter is required when using ModuleInfo parameter set"
                }
                $ImportPath = Join-Path $ModuleInformation.ProjectPath '*.psd1'
                $DisplayName = $ModuleInformation.ModuleName
            }
            'Path' {
                if (-not (Test-Path -Path $Path)) {
                    throw "Module path '$Path' does not exist"
                }
                $ImportPath = $Path
                $DisplayName = [System.IO.Path]::GetFileNameWithoutExtension($Path)
            }
            'ModuleName' {
                $ImportPath = $ModuleName
                $DisplayName = $ModuleName
            }
        }

        Write-Host "Importing module: $DisplayName" -ForegroundColor Yellow
        Write-Verbose "Import path: $ImportPath"

        # Import the module with detailed error reporting
        try {
            $ImportParams = @{
                Name        = $ImportPath
                Force       = $Force.IsPresent
                ErrorAction = 'Stop'
            }

            Import-Module @ImportParams
            Write-Host "  Successfully imported module: $DisplayName" -ForegroundColor Green

            # Show module information if requested
            if ($ShowInformation) {
                $ImportedModule = Get-Module -Name $DisplayName
                if ($ImportedModule) {
                    Write-Host "Module Information:" -ForegroundColor Cyan
                    Write-Host "  Name: $($ImportedModule.Name)" -ForegroundColor White
                    Write-Host "  Version: $($ImportedModule.Version)" -ForegroundColor White
                    Write-Host "  Path: $($ImportedModule.Path)" -ForegroundColor White
                    Write-Host "  Exported Functions: $($ImportedModule.ExportedFunctions.Count)" -ForegroundColor White
                    Write-Host "  Exported Cmdlets: $($ImportedModule.ExportedCmdlets.Count)" -ForegroundColor White
                    Write-Host "  Exported Aliases: $($ImportedModule.ExportedAliases.Count)" -ForegroundColor White

                    if ($ModuleInformation) {
                        Write-Host "  PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor White
                        Write-Host "  PowerShell Edition: $($PSVersionTable.PSEdition)" -ForegroundColor White
                    }
                }
            }

        } catch {
            $err = $_
            $errorMessage = @"
Failed to import module '$DisplayName' from path '$ImportPath'.
Error: $($err.Exception.Message)
FQID: $($err.FullyQualifiedErrorId)
Category: $($err.CategoryInfo)
Position: $($err.InvocationInfo.PositionMessage)
"@

            Write-Error -Message $errorMessage

            if ($err.ScriptStackTrace) {
                Write-Error -Message "Stack:`n$($err.ScriptStackTrace)"
            }

            # Additional troubleshooting information
            if ($PSCmdlet.ParameterSetName -eq 'Path') {
                $fileInfo = Get-Item -Path $Path -ErrorAction SilentlyContinue
                if ($fileInfo) {
                    Write-Host "File Information:" -ForegroundColor Yellow
                    Write-Host "  Size: $($fileInfo.Length) bytes" -ForegroundColor White
                    Write-Host "  Last Modified: $($fileInfo.LastWriteTime)" -ForegroundColor White
                    Write-Host "  Extension: $($fileInfo.Extension)" -ForegroundColor White
                }
            }

            throw
        }

    } catch {
        Write-Error "Failed to test module import: $($_.Exception.Message)"
        throw
    }
}
