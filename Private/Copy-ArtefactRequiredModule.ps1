function Copy-ArtefactRequiredModule {
    [CmdletBinding()]
    param(
        [switch] $Enabled,
        [Array] $RequiredModules,
        [string] $FolderPath
    )
    if (-not $Enabled) {
        return
    }
    if (-not (Test-Path -LiteralPath $FolderPath)) {
        New-Item -ItemType Directory -Path $FolderPath -Force
    }
    foreach ($Module in $RequiredModules) {
        if ($Module.ModuleName) {
            Write-TextWithTime -PreAppend Addition -Text "Copying required module $($Module.ModuleName)" -Color Yellow {
                # Write-Text "   [>] Copying required module $($Module.ModuleName)" -Color Yellow
                $ModulesFound = Get-Module -ListAvailable -Name $Module.ModuleName
                if ($ModulesFound.Count -gt 0) {
                    $PathToPSD1 = if ($Module.ModuleVersion -eq 'Latest') {
                        $ModulesFound[0].Path
                    } else {
                        $FoundModule = foreach ($M in $ModulesFound) {
                            if ($M.Version -eq $Module.ModuleVersion) {
                                $M.Path
                                break
                            }
                        }
                        if (-not $FoundModule) {
                            # we tried to find exact version, but it was not found
                            # we use the latest version instead
                            $ModulesFound[0].Path
                        } else {
                            $FoundModule
                        }
                    }
                    $FolderToCopy = [System.IO.Path]::GetDirectoryName($PathToPSD1)
                    $ItemInformation = Get-Item -LiteralPath $FolderToCopy

                    #try {
                    if ($ItemInformation.Name -ne $Module.ModuleName) {
                        $NewPath = [io.path]::Combine($FolderPath, $Module.ModuleName)
                        if (Test-Path -LiteralPath $NewPath) {
                            Remove-Item -LiteralPath $NewPath -Recurse -Force -ErrorAction Stop
                        }
                        Copy-Item -LiteralPath $FolderToCopy -Destination $NewPath -Recurse -Force -ErrorAction Stop
                    } else {
                        Copy-Item -LiteralPath $FolderToCopy -Destination $FolderPath -Recurse -Force
                    }
                    #} catch {
                    #   $ErrorMessage = $_.Exception.Message
                    #    Write-Text "[-] Start-ArtefactsBuilding - Copying final artefact module $($Module.ModuleName) to $FolderPathReleasesUnpacked failed. Error: $ErrorMessage" -Color Red
                    #    return $false
                    #}
                }
            } -SpacesBefore '   '
        }
    }
}