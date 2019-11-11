function Start-ModuleBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration
    )

    $DestinationPaths = @{ }
    if ($Configuration.Information.Manifest.CompatiblePSEditions) {
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Desktop') {
            $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        }
        if ($Configuration.Information.Manifest.CompatiblePSEditions -contains 'Core') {
            $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
        }
    } else {
        # Means missing from config - send to both
        $DestinationPaths.Desktop = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        $DestinationPaths.Core = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
    }

    [string] $Random = Get-Random 10000000000
    [string] $FullModulePath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
    [string] $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName + "_TEMP_$Random"
    [string] $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
    [string] $ProjectName = $Configuration.Information.ModuleName

    Write-Text '----------------------------------------------------'
    Write-Text "[i] Project Name: $ProjectName" -Color Yellow
    Write-Text "[i] Full module temporary path: $FullModulePath" -Color Yellow
    Write-Text "[i] Full project path: $FullProjectPath" -Color Yellow
    Write-Text "[i] Full temporary path: $FullTemporaryPath" -Color Yellow
    Write-Text "[i] PSScriptRoot: $PSScriptRoot" -Color Yellow
    Write-Text "[i] Current PSEdition: $PSEdition" -Color Yellow
    Write-Text "[i] Destination Desktop: $($DestinationPaths.Desktop)" -Color Yellow
    Write-Text "[i] Destination Core: $($DestinationPaths.Desktop)" -Color Yellow
    Write-Text '----------------------------------------------------'

    $CurrentLocation = (Get-Location).Path
    Set-Location -Path $FullProjectPath

    # Remove-Directory $FullModulePathDelete
    Remove-Directory $FullModulePath
    Remove-Directory $FullTemporaryPath
    Add-Directory $FullModulePath
    Add-Directory $FullTemporaryPath

    # $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates', 'Resources'

    $LinkDirectories = @()
    $LinkPrivatePublicFiles = @()

    # Fix required fields:
    $Configuration.Information.Manifest.RootModule = "$($ProjectName).psm1"
    $Configuration.Information.Manifest.FunctionsToExport = @() #$FunctionToExport
    # Cmdlets to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no cmdlets to export.
    $Configuration.Information.Manifest.CmdletsToExport = @()
    # Variables to export from this module
    $Configuration.Information.Manifest.VariablesToExport = @()
    # Aliases to export from this module, for best performance, do not use wildcards and do not delete the entry, use an empty array if there are no aliases to export.
    $Configuration.Information.Manifest.AliasesToExport = @()

    $Exclude = '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'

    if ($Configuration.Steps.BuildModule) {
        $PreparingFilesTime = Write-Text "[+] Preparing files and folders" -Start

        if ($PSEdition -eq 'core') {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude -FollowSymlink
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse -FollowSymlink
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude $Exclude -FollowSymlink | Get-ChildItem -File -Recurse -FollowSymlink
            $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include '*.psm1', '*.psd1', 'License*' -File -FollowSymlink
        } else {
            $Directories = @(
                $TempDirectories = Get-ChildItem -Path $FullProjectPath -Directory -Exclude $Exclude
                @(
                    $TempDirectories
                    $TempDirectories | Get-ChildItem -Directory -Recurse
                )
            )
            $Files = Get-ChildItem -Path $FullProjectPath -Exclude '.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs' | Get-ChildItem -File -Recurse
            $FilesRoot = Get-ChildItem -Path "$FullProjectPath\*" -Include '*.psm1', '*.psd1', 'License*' -File
        }
        $LinkDirectories = @(
            foreach ($directory in $Directories) {
                $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
                $RelativeDirectoryPath = "$RelativeDirectoryPath\"
                $RelativeDirectoryPath
            }
        )
        $AllFiles = foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RelativeFilePath
        }
        $RootFiles = foreach ($File in $FilesRoot) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RelativeFilePath
        }
        # Link only files in Root Directory

        $LinkFilesRoot = @(
            foreach ($File in $RootFiles | Sort-Object -Unique) {
                switch -Wildcard ($file) {
                    '*.psd1' {
                        #Write-Color $File -Color Red
                        # Add-ObjectTo -Object $File -Type 'Root Files List'
                        $File
                    }
                    '*.psm1' {
                        # Write-Color $File.FulllName -Color cd
                        #Add-ObjectTo -Object $File -Type 'Root Files List'
                        $File
                    }
                    'License*' {
                        #Add-ObjectTo -Object $File -Type 'Root Files List'
                        $File
                    }
                }
            }
        )

        # Link only files from subfolers
        $LinkPrivatePublicFiles = @(
            foreach ($file in $AllFiles | Sort-Object -Unique) {
                switch -Wildcard ($file) {
                    '*.ps1' {
                        Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Private', 'Public', 'Enums'
                        continue
                    }
                    '*.*' {
                        Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Images\', 'Resources\', 'Templates\', 'Bin\', 'Lib\'
                        continue
                    }
                }
            }
        )
        $LinkPrivatePublicFiles = $LinkPrivatePublicFiles | Select-Object -Unique

        Write-Text -End -Time $PreparingFilesTime
        $AliasesAndFunctions = Write-TextWithTime -Text '[+] Preparing function and aliases names' {
            Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Files $Files #-Folder $Configuration.Information.AliasesToExport
        }

        if ($AliasesAndFunctions.Function) {
            $Configuration.Information.Manifest.FunctionsToExport = $AliasesAndFunctions.Function
        }
        if ($AliasesAndFunctions.Alias) {
            $Configuration.Information.Manifest.AliasesToExport = $AliasesAndFunctions.Alias
        }

        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.ScriptsToProcess)) {
            $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)\"
            $FilesEnums = @(
                $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithEnums) }
            )

            if ($FilesEnums.Count -gt 0) {
                #Write-Verbose "ScriptsToProcess export: $FilesEnums"
                Write-TextWithTime -Text "[+] ScriptsToProcess export $FilesEnums"
                $Configuration.Information.Manifest.ScriptsToProcess = $FilesEnums
            }
            #}
        }

        $PSD1FilePath = "$FullProjectPath\$ProjectName.psd1"
        New-PersonalManifest -Configuration $Configuration -ManifestPath $PSD1FilePath -AddScriptsToProcess

        Format-Code -FilePath $PSD1FilePath -FormatCode $Configuration.Options.Standard.FormatCodePSD1
    }
    if ($Configuration.Steps.BuildModule.Merge) {
        foreach ($Directory in $LinkDirectories) {
            $Dir = "$FullTemporaryPath\$Directory"
            Add-Directory $Dir
        }
        # Workaround to link files that are not ps1/psd1
        $LinkDirectoriesWithSupportFiles = $LinkDirectories | Where-Object { $_ -ne 'Public\' -and $_ -ne 'Private\' }
        foreach ($Directory in $LinkDirectoriesWithSupportFiles) {
            $Dir = "$FullModulePath\$Directory"
            Add-Directory $Dir
        }

        $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
        Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
        Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
        Write-Text -End -Time $LinkingFilesTime

        # Workaround to link files that are not ps1/psd1
        $FilesToLink = $LinkPrivatePublicFiles | Where-Object { $_ -notlike '*.ps1' -and $_ -notlike '*.psd1' }
        Set-LinkedFiles -LinkFiles $FilesToLink -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath

        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesCore)) {
            $StartsWithCore = "$($Configuration.Information.LibrariesCore)\"
            $FilesLibrariesCore = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithCore) }
            #$FilesLibrariesCore
        }
        if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.LibrariesDefault)) {
            $StartsWithDefault = "$($Configuration.Information.LibrariesDefault)\"
            $FilesLibrariesDefault = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithDefault) }
            #$FilesLibrariesDefault
        }

        Merge-Module -ModuleName $ProjectName `
            -ModulePathSource $FullTemporaryPath `
            -ModulePathTarget $FullModulePath `
            -Sort $Configuration.Options.Merge.Sort `
            -FunctionsToExport $Configuration.Information.Manifest.FunctionsToExport `
            -AliasesToExport $Configuration.Information.Manifest.AliasesToExport `
            -LibrariesCore $FilesLibrariesCore `
            -LibrariesDefault $FilesLibrariesDefault `
            -FormatCodePSM1 $Configuration.Options.Merge.FormatCodePSM1 `
            -FormatCodePSD1 $Configuration.Options.Merge.FormatCodePSD1 `
            -Configuration $Configuration

    } else {
        foreach ($Directory in $LinkDirectories) {
            $Dir = "$FullModulePath\$Directory"
            Add-Directory $Dir
        }
        $LinkingFilesTime = Write-Text "[+] Linking files from root and sub directories" -Start
        Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        Write-Text -End -Time $LinkingFilesTime
    }


    # Revers Path to current locatikon
    Set-Location -Path $CurrentLocation

    if ($DestinationPaths.Desktop) {
        Write-TextWithTime -Text "[+] Copy module to PowerShell 5 destination: $($DestinationPaths.Desktop)" {
            Remove-Directory -Directory $DestinationPaths.Desktop
            Add-Directory -Directory $DestinationPaths.Desktop
            Get-ChildItem -LiteralPath $FullModulePath | Copy-Item -Destination $DestinationPaths.Desktop -Recurse
        }
    }
    if ($DestinationPaths.Core) {
        Write-TextWithTime -Text "[+] Copy module to PowerShell 6/7 destination: $($DestinationPaths.Core)" {
            Remove-Directory -Directory $DestinationPaths.Core
            Add-Directory -Directory $DestinationPaths.Core
            Get-ChildItem -LiteralPath $FullModulePath | Copy-Item -Destination $DestinationPaths.Core -Recurse
        }
    }

    if ($Configuration.Steps.BuildModule.Releases) {
        $TagName = "v$($Configuration.Information.Manifest.ModuleVersion)"
        $FileName = -join ("$TagName", '.zip')
        $FolderPathReleases = [System.IO.Path]::Combine($FullProjectPath, 'Releases')
        $ZipPath = [System.IO.Path]::Combine($FullProjectPath, 'Releases', $FileName)

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
        if ($Configuration.Steps.PublishModule.GitHub) {
            if ($Configuration.Options.GitHub.FromFile) {
                $GitHubAccessToken = Get-Content -LiteralPath $Configuration.Options.GitHub.ApiKey
            } else {
                $GitHubAccessToken = $Configuration.Options.GitHub.ApiKey
            }
            if ($GitHubAccessToken) {
                if ($Configuration.Options.GitHub.RepositoryName) {
                    $GitHubRepositoryName = $Configuration.Options.GitHub.RepositoryName
                } else {
                    $GitHubRepositoryName = $ProjectName
                }
                if (Test-Path -LiteralPath $ZipPath) {
                    <#
                    $newGitHubReleaseParameters =
                    @{
                        GitHubUsername = 'deadlydog'
                        GitHubRepositoryName = 'New-GitHubRelease'
                        GitHubAccessToken = 'SomeLongHexidecimalString'
                        ReleaseName = "New-GitHubRelease v1.0.0"
                        TagName = "v1.0.0"
                        ReleaseNotes = "This release contains the following changes: ..."
                        AssetFilePaths = @('C:\MyProject\Installer.exe','C:\MyProject\Documentation.md')
                        IsPreRelease = $false
                        IsDraft = $true	# Set to true when testing so we don't publish a real release (visible to everyone) by accident.
                    }
                    #>

                    if ($Configuration.Steps.PublishModule.Prerelease -ne '') {
                        $IsPreRelease = $true
                    } else {
                        $IsPreRelease = $false
                    }

                    $StatusGithub = New-GitHubRelease -GitHubUsername $Configuration.Options.GitHub.UserName -GitHubRepositoryName $GitHubRepositoryName -GitHubAccessToken $GitHubAccessToken -TagName $TagName -AssetFilePaths $ZipPath -IsPreRelease $IsPreRelease
                    if ($StatusGithub.ReleaseCreationSucceeded -and $statusGithub.Succeeded) {
                        $GithubColor = 'Green'
                        $GitHubText = '+'
                    } else {
                        $GithubColor = 'Red'
                        $GitHubText = '-'
                    }

                    Write-Text "[$GitHubText] GitHub Release Creation Status: $($StatusGithub.ReleaseCreationSucceeded)" -Color $GithubColor
                    Write-Text "[$GitHubText] GitHub Release Succeeded: $($statusGithub.Succeeded)" -Color $GithubColor
                    Write-Text "[$GitHubText] GitHub Release Asset Upload Succeeded: $($statusGithub.AllAssetUploadsSucceeded)" -Color $GithubColor
                    Write-Text "[$GitHubText] GitHub Release URL: $($statusGitHub.ReleaseUrl)" -Color $GithubColor
                    if ($statusGithub.ErrorMessage) {
                        Write-Text "[$GitHubText] GitHub Release ErrorMessage: $($statusGithub.ErrorMessage)" -Color $GithubColor
                    }
                }
            }
        }
    }

    if ($Configuration.Steps.PublishModule.Enabled) {
        if ($Configuration.Options.PowerShellGallery.FromFile) {
            $ApiKey = Get-Content -Path $Configuration.Options.PowerShellGallery.ApiKey
            New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
        } else {
            New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Options.PowerShellGallery.ApiKey -RequireForce $Configuration.Steps.PublishModule.RequireForce
        }
    }

    # Import Modules Section
    if ($Configuration) {

        $TemporaryVerbosePreference = $VerbosePreference
        $VerbosePreference = $false

        if ($Configuration.Options.ImportModules.RequiredModules) {
            Write-TextWithTime -Text '[+] Importing modules - REQUIRED' {
                foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                    Import-Module -Name $Module -Force -ErrorAction Stop -Verbose:$false  #$Configuration.Options.ImportModules.Verbose
                }
            }
        }
        if ($Configuration.Options.ImportModules.Self) {
            Write-TextWithTime -Text '[+] Importing module - SELF' {

                Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$false
            }
        }
        $VerbosePreference = $TemporaryVerbosePreference

        if ($Configuration.Steps.BuildDocumentation) {
            $WarningVariablesMarkdown = @()
            $DocumentationPath = "$FullProjectPath\$($Configuration.Options.Documentation.Path)"
            $ReadMePath = "$FullProjectPath\$($Configuration.Options.Documentation.PathReadme)"
            Write-Text "[+] Generating documentation to $DocumentationPath with $ReadMePath" -Color Yellow

            if (-not (Test-Path -Path $DocumentationPath)) {
                $null = New-Item -Path "$FullProjectPath\Docs" -ItemType Directory -Force
            }
            $Files = Get-ChildItem -Path $DocumentationPath
            if ($Files.Count -gt 0) {
                $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
            } else {
                $null = New-MarkdownHelp -Module $ProjectName -WithModulePage -OutputFolder $DocumentationPath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
                $null = Move-Item -Path "$DocumentationPath\$ProjectName.md" -Destination $ReadMePath
                #Start-Sleep -Seconds 1
                # this is temporary workaround - due to diff output on update
                if ($Configuration.Options.Documentation.UpdateWhenNew) {
                    $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop -WarningVariable +WarningVariablesMarkdown -WarningAction SilentlyContinue
                }
                #
            }
            foreach ($_ in $WarningVariablesMarkdown) {
                Write-Text "[-] Documentation warning: $_" -Color Yellow
            }
        }
    }
    # Cleanup temp directory
    Write-Text "[+] Cleaning up directories created in TEMP directory" -Color Yellow
    Remove-Directory $FullModulePath
    Remove-Directory $FullTemporaryPath
}