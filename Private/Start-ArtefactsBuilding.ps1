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
        $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
        $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)

        if ($Configuration.Steps.BuildModule.Releases) {
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
                    $ArtefactsPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Configuration.Steps.BuildModule.ReleasesUnpacked.Path)
                } else {
                    $ArtefactsPath = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked')
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.IncludeTagName) {
                    $FolderPathReleasesUnpacked = [System.IO.Path]::Combine($ArtefactsPath, $TagName )
                } else {
                    $FolderPathReleasesUnpacked = $ArtefactsPath
                }
            } else {
                $FolderPathReleasesUnpacked = [System.IO.Path]::Combine($FullProjectPath, 'ReleasesUnpacked', $TagName )
            }
            Write-TextWithTime -Text "[+] Copying final merged release to $FolderPathReleasesUnpacked" {
                try {
                    if (Test-Path -Path $FolderPathReleasesUnpacked) {
                        Remove-ItemAlternative -LiteralPath $FolderPathReleasesUnpacked -SkipFolder
                    }
                    $null = New-Item -ItemType Directory -Path $FolderPathReleasesUnpacked -Force
                    if ($DestinationPaths.Desktop) {
                        Copy-Item -LiteralPath $DestinationPaths.Desktop -Recurse -Destination $FolderPathReleasesUnpacked -Force
                    }
                    if ($DestinationPaths.Core -and -not $DestinationPaths.Desktop) {
                        Copy-Item -LiteralPath $DestinationPaths.Core -Recurse -Destination $FolderPathReleasesUnpacked -Force
                    }
                } catch {
                    $ErrorMessage = $_.Exception.Message
                    #Write-Warning "Merge module on file $FilePath failed. Error: $ErrorMessage"
                    Write-Host # This is to add new line, because the first line was opened up.
                    Write-Text "[-] Format-Code - Copying final merged release to $FolderPathReleasesUnpacked failed. Error: $ErrorMessage" -Color Red
                    Exit
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.RequiredModules) {
                    foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                        if ($Module.ModuleName) {
                            $ModulesFound = Get-Module -ListAvailable -Name $Module.ModuleName
                            if ($ModulesFound.Count -gt 0) {
                                $PathToPSD1 = if ($Module.ModuleVersion -eq 'Latest') {
                                    $ModulesFound[0].Path
                                } else {
                                    foreach ($M in $ModulesFound) {
                                        if ($M.Version -eq $Module.ModuleVersion) {
                                            $M.Path
                                            break
                                        }
                                    }
                                }
                                $FolderToCopy = [System.IO.Path]::GetDirectoryName($PathToPSD1)
                                $ItemInformation = Get-Item -LiteralPath $FolderToCopy

                                #copy-item .\documents\ -destination .\my-backup-$(Get-Date -format "yyyy_MM_dd_hh_mm_ss")


                                if ($ItemInformation.DirectoryName -ne $Module.ModuleName) {
                                    $NewPath = [io.path]::Combine($FolderPathReleasesUnpacked, $Module.ModuleName)
                                    Copy-Item -LiteralPath $FolderToCopy -Destination $NewPath -Recurse
                                } else {
                                    Copy-Item -LiteralPath $FolderToCopy -Destination $FolderPathReleasesUnpacked -Recurse
                                }
                            }
                        }
                    }
                }
                if ($Configuration.Steps.BuildModule.ReleasesUnpacked.DirectoryOutput) {

                }
            }
        }
    }
}