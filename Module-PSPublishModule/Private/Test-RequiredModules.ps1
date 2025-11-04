function Test-RequiredModules {
    <#
    .SYNOPSIS
    Tests and installs required modules for a PowerShell project

    .DESCRIPTION
    Validates that all required modules are installed and meet version requirements.
    Automatically installs missing modules or updates modules that don't meet version constraints.
    Supports additional modules beyond those specified in the manifest.

    .PARAMETER ModuleInformation
    Module information object returned by Get-ModuleInformation

    .PARAMETER AdditionalModules
    Additional modules to install beyond those in the manifest

    .PARAMETER SkipModules
    Array of module names to skip during installation

    .PARAMETER Force
    Force installation/update of modules even if they appear to meet requirements

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    Test-RequiredModules -ModuleInformation $moduleInfo

    .EXAMPLE
    $moduleInfo = Get-ModuleInformation -Path $PSScriptRoot
    Test-RequiredModules -ModuleInformation $moduleInfo -AdditionalModules @('Pester', 'PSWriteColor') -SkipModules @('CertNoob')
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [hashtable]$ModuleInformation,

        [Parameter()]
        [string[]]$AdditionalModules = @(),

        [Parameter()]
        [string[]]$SkipModules = @(),

        [Parameter()]
        [switch]$Force
    )

    try {
        # Combine manifest modules with additional modules
        $RequiredModules = @()
        $RequiredModules += $AdditionalModules

        if ($ModuleInformation.RequiredModules) {
            $RequiredModules += $ModuleInformation.RequiredModules
        }

        if ($RequiredModules.Count -eq 0) {
            Write-Warning "No required modules specified for module '$($ModuleInformation.ModuleName)'"
            return
        }

        Write-Host "Checking required modules for: $($ModuleInformation.ModuleName)" -ForegroundColor Yellow

        foreach ($Module in $RequiredModules) {
            $ModuleName = $null
            $RequiredVersion = $null
            $MinimumVersion = $null
            $MaximumVersion = $null

            # Parse module specification
            if ($Module -is [System.Collections.IDictionary]) {
                $ModuleName = $Module.ModuleName
                $RequiredVersion = $Module.RequiredVersion
                $MinimumVersion = $Module.ModuleVersion
                $MaximumVersion = $Module.MaximumVersion
            } else {
                $ModuleName = $Module
            }

            # Skip modules in the skip list
            if ($ModuleName -in $SkipModules) {
                Write-Host "  [-] Skipping: $ModuleName" -ForegroundColor Gray
                continue
            }

            Write-Host "  [>] Checking: $ModuleName" -ForegroundColor Cyan

            # Check if module is installed
            $ExistingModules = Get-Module -ListAvailable -Name $ModuleName -ErrorAction SilentlyContinue

            if (-not $ExistingModules) {
                Write-Warning "    Installing $ModuleName from PowerShell Gallery"

                $InstallParams = @{
                    Name               = $ModuleName
                    Force              = $true
                    SkipPublisherCheck = $true
                    ErrorAction        = 'Stop'
                }

                if ($RequiredVersion) {
                    $InstallParams.RequiredVersion = $RequiredVersion
                    Write-Host "      Installing exact version: $RequiredVersion" -ForegroundColor Yellow
                } elseif ($MinimumVersion) {
                    $InstallParams.MinimumVersion = $MinimumVersion
                    Write-Host "      Installing minimum version: $MinimumVersion" -ForegroundColor Yellow
                }

                try {
                    Install-Module @InstallParams
                    Write-Host "      Successfully installed $ModuleName" -ForegroundColor Green
                } catch {
                    Write-Error "      Failed to install $ModuleName`: $($_.Exception.Message)"
                    throw
                }
            } else {
                # Module exists, check version requirements
                $LatestInstalled = ($ExistingModules | Sort-Object Version -Descending)[0]
                Write-Host "      Found installed version: $($LatestInstalled.Version)" -ForegroundColor Green

                if ($Force -or $RequiredVersion -or $MinimumVersion -or $MaximumVersion) {
                    $VersionCheck = Compare-ModuleVersion -InstalledVersion $LatestInstalled.Version -RequiredVersion $RequiredVersion -MinimumVersion $MinimumVersion -MaximumVersion $MaximumVersion

                    if ($Force -or $VersionCheck.NeedsInstall) {
                        if ($Force) {
                            Write-Warning "      Force parameter specified - reinstalling $ModuleName"
                        } else {
                            Write-Warning "      $($VersionCheck.Reason)"
                        }

                        Write-Host "      Updating $ModuleName to version $($VersionCheck.InstallVersion)" -ForegroundColor Yellow

                        $InstallParams = @{
                            Name               = $ModuleName
                            Force              = $true
                            SkipPublisherCheck = $true
                            ErrorAction        = 'Stop'
                        }

                        if ($RequiredVersion) {
                            $InstallParams.RequiredVersion = $RequiredVersion
                        } elseif ($VersionCheck.InstallVersion) {
                            $InstallParams.MinimumVersion = $VersionCheck.InstallVersion
                        }

                        try {
                            Install-Module @InstallParams
                            Write-Host "      Successfully updated $ModuleName" -ForegroundColor Green
                        } catch {
                            Write-Error "      Failed to update $ModuleName`: $($_.Exception.Message)"
                            throw
                        }
                    } elseif ($VersionCheck.Reason) {
                        Write-Verbose "      $ModuleName $($VersionCheck.Reason)"
                    } else {
                        Write-Host "      Version requirements satisfied" -ForegroundColor Green
                    }
                } else {
                    Write-Host "      No specific version requirements" -ForegroundColor Green
                }
            }
        }

        Write-Host "Module dependency check completed successfully" -ForegroundColor Green
    } catch {
        Write-Error "Failed to test required modules: $($_.Exception.Message)"
        throw
    }
}
