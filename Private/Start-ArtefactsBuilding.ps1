function Start-ArtefactsBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [string] $FullProjectPath,
        [System.Collections.IDictionary] $DestinationPaths
    )
    if ($Configuration.Steps.BuildModule.Releases -or $Configuration.Steps.BuildModule.ReleasesUnpacked) {
        $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
        $FileName = -join ("$TagName", '.zip')

        if ($Configuration.Steps.BuildModule.Releases -eq $true -or $Configuration.Steps.BuildModule.Releases.Enabled) {
            if ($Configuration.Steps.BuildModule.Releases -is [System.Collections.IDictionary]) {
                if ($Configuration.Steps.BuildModule.Releases.Path) {
                    if ($Configuration.Steps.BuildModule.Releases.Relative -eq $false) {
                        $FolderPathReleases = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.Releases.Path)
                    } else {
                        $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.Releases.Path)
                    }
                } else {
                    $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
                }
            } else {
                # default values
                $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
            }
            $ZipPath = [System.IO.Path]::Combine($FolderPathReleases, $FileName)

            Write-TextWithTime -Text "[+] Compressing final merged release $ZipPath" {
                $null = New-Item -ItemType Directory -Path $FolderPathReleases -Force
                if ($DestinationPaths.Desktop) {
                    $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Desktop, '*')
                    Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
                }
                if ($DestinationPaths.Core -and -not $DestinationPaths.Desktop) {
                    $CompressPath = [System.IO.Path]::Combine($DestinationPaths.Core, '*')
                    Compress-Archive -Path $CompressPath -DestinationPath $ZipPath -Force
                }
            }
        }
        if ($Configuration.Steps.BuildModule.ReleasesUnpacked -eq $true -or $Configuration.Steps.BuildModule.ReleasesUnpacked.Enabled) {
            if ($Configuration.Steps.BuildModule.ReleasesUnpacked -is [System.Collections.IDictionary]) {
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.Path) {
                    if ($Configuration.Steps.BuildModule.ReleasesUnpacked.Relative -eq $false) {
                        $ArtefactsPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.ReleasesUnpacked.Path)
                    } else {
                        $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.ReleasesUnpacked.Path)
                    }
                } else {
                    $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked')
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path) {
                    if ($Configuration.Steps.BuildModule.ReleasesUnpacked.Relative -eq $false) {
                        $RequiredModulesPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path)
                    } else {
                        $RequiredModulesPath = [System.IO.Path]::Combine($FullProjectPath, $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Path)
                    }
                } else {
                    $RequiredModulesPath = $ArtefactsPath
                }
                $CurrentModulePath = $RequiredModulesPath
                $FolderPathReleasesUnpacked = $ArtefactsPath
            } else {
                # default values
                $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName)
                $FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
                $RequiredModulesPath = $ArtefactsPath
                $CurrentModulePath = $ArtefactsPath
            }
            Write-TextWithTime -Text "Copying final merged release to $ArtefactsPath" -PreAppend Plus {
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.IncludeTagName) {
                    $NameOfDestination = [io.path]::Combine($CurrentModulePath, $Configuration.Information.ModuleName, $TagName)
                } else {
                    $NameOfDestination = [io.path]::Combine($CurrentModulePath, $Configuration.Information.ModuleName)
                }
                Write-Text "   [>] Copying module main module to $NameOfDestination" -Color Yellow
                try {
                    if (Test-Path -Path $NameOfDestination) {
                        Remove-ItemAlternative -LiteralPath $NameOfDestination #-SkipFolder
                    }
                    $null = New-Item -ItemType Directory -Path $FolderPathReleasesUnpacked -Force

                    if ($DestinationPaths.Desktop) {
                        Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $NameOfDestination -Force
                    } elseif ($DestinationPaths.Core) {
                        Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $NameOfDestination -Force
                    }
                } catch {
                    $ErrorMessage = $_.Exception.Message
                    #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                    Write-Host # This is to add new line, because the first line was opened up.
                    Write-Text "[-] Start-ArtefactsBuilding - Copying final merged release to $FolderPathReleasesUnpacked failed. Error: $ErrorMessage" -Color Red
                    return $false
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules -eq $true -or $Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules.Enabled) {
                    foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                        if ($Module.ModuleName) {
                            Write-Text "   [>] Copying required module $($Module.ModuleName)" -Color Yellow
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

                                try {
                                    if ($ItemInformation.Name -ne $Module.ModuleName) {
                                        $NewPath = [io.path]::Combine($RequiredModulesPath, $Module.ModuleName)
                                        if (Test-Path -LiteralPath $NewPath) {
                                            Remove-Item -LiteralPath $NewPath -Recurse -Force -ErrorAction Stop
                                        }
                                        #New-Item -Path $NewPath -ItemType Directory -Force -ErrorAction Stop
                                        Copy-Item -LiteralPath "$FolderToCopy" -Destination "$NewPath" -Recurse -Force -ErrorAction Stop
                                    } else {
                                        Copy-Item -LiteralPath $FolderToCopy -Destination $RequiredModulesPath -Recurse -Force
                                    }
                                } catch {
                                    $ErrorMessage = $_.Exception.Message
                                    Write-Text "[-] Start-ArtefactsBuilding - Copying final artefact module $($Module.ModuleName) to $FolderPathReleasesUnpacked failed. Error: $ErrorMessage" -Color Red
                                    return $false
                                }
                            }
                        }
                    }
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.DirectoryOutput) {

                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.FilesOutput) {
                    $FilesOutput = $Configuration.Steps.BuildModule.ReleasesUnpacked.FilesOutput
                    foreach ($File in $FilesOutput.Keys) {
                        if ($FilesOutput[$File] -is [string]) {
                            $FullFilePath = [System.IO.Path]::Combine($FullProjectPath, $File)
                            if (Test-Path -Path $FullFilePath) {
                                $DestinationPath = [System.IO.Path]::Combine($FolderPathReleasesUnpacked, $FilesOutput[$File])
                                $ResolvedDestination = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($DestinationPath)
                                Write-TextWithTime -Text "Copying file $FullFilePath to $ResolvedDestination" {
                                    $DirectoryPath = [Io.Path]::GetDirectoryName($ResolvedDestination)
                                    $null = New-Item -ItemType Directory -Force -ErrorAction Stop -Path $DirectoryPath
                                    Copy-Item -LiteralPath $FullFilePath -Destination $ResolvedDestination -Force -ErrorAction Stop
                                } -PreAppend Plus -SpacesBefore "   "
                            }
                        } elseif ($FilesOutput[$File] -is [System.Collections.IDictionary]) {
                            if ($FilesOutput[$File].Enabled -eq $true) {

                            }
                        }
                    }
                }
            }
        }
    }
}