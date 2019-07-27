function Start-ModuleBuilding {
    [CmdletBinding()]
    param(
        [System.Collections.IDictionary] $Configuration,
        [switch] $Core
    )
    if ($Core) {
        [string] $FullModulePath = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
        # [string] $FullModulePathDelete = [IO.path]::Combine($Configuration.Information.DirectoryModulesCore, $Configuration.Information.ModuleName)
    } else {
        [string] $FullModulePath = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
        # [string] $FullModulePathDelete = [IO.path]::Combine($Configuration.Information.DirectoryModules, $Configuration.Information.ModuleName)
    }
    [string] $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
    [string] $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
    [string] $ProjectName = $Configuration.Information.ModuleName

    Write-Verbose '----------------------------------------------------'
    Write-Verbose "Project Name: $ProjectName"
    Write-Verbose "Full module path: $FullModulePath"
    Write-Verbose "Full project path: $FullProjectPath"
    Write-Verbose "Full module path to delete: $FullModulePathDelete"
    Write-Verbose "Full temporary path: $FullTemporaryPath"
    Write-Verbose "PSScriptRoot: $PSScriptRoot"
    Write-Verbose "PSEdition: $PSEdition"
    Write-Verbose '----------------------------------------------------'

    $CurrentLocation = (Get-Location).Path
    Set-Location -Path $FullProjectPath

    # Remove-Directory $FullModulePathDelete
    Remove-Directory $FullModulePath
    Remove-Directory $FullTemporaryPath
    Add-Directory $FullModulePath
    Add-Directory $FullTemporaryPath

    $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates', 'Resources'

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


    if ($Configuration.Steps.BuildModule) {

        if ($PSEdition -eq 'core') {
            $Directories = Write-TextWithTime -Text "Getting directories list" {
                Get-ChildItem -Path $FullProjectPath -Directory -Recurse -FollowSymlink
            }
            $Files = Write-TextWithTime -Text "Getting files list" {
                Get-ChildItem -Path $FullProjectPath -File -Recurse -FollowSymlink
            }
            $FilesRoot = Write-TextWithTime -Text "Getting files list - root" {
                Get-ChildItem -Path $FullProjectPath -File -FollowSymlink
            }
        } else {
            $Directories = Write-TextWithTime -Text "Getting directories list" {
                Get-ChildItem -Path $FullProjectPath -Directory -Recurse
            }
            $Files = Write-TextWithTime -Text "Getting files list" {
                Get-ChildItem -Path $FullProjectPath -File -Recurse
            }
            $FilesRoot = Write-TextWithTime -Text "Getting files list - root" {
                Get-ChildItem -Path $FullProjectPath -File
            }
        }

        $LinkDirectories = Write-TextWithTime -Text "Adding Directories to Directory List" {
            foreach ($directory in $Directories) {
                $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
                $RelativeDirectoryPath = "$RelativeDirectoryPath\"
                foreach ($LookupDir in $DirectoryTypes) {
                    #Write-Verbose "New-PrepareModule - RelativeDirectoryPath: $RelativeDirectoryPath LookupDir: $LookupDir\"
                    if ($RelativeDirectoryPath -like "$LookupDir\*" ) {
                        # Add-ObjectTo -Object $RelativeDirectoryPath -Type 'Directory List'

                        $RelativeDirectoryPath
                    }
                }
            }
        }
        $AllFiles = foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RelativeFilePath
        }
        $RootFiles = foreach ($File in $FilesRoot) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RelativeFilePath
        }
        # Link only files in Root Directory
        $LinkFilesRoot = Write-TextWithTime -Text "Adding Files to Root Files List" {
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
        }
        # Link only files from subfolers
        $LinkPrivatePublicFiles = Write-TextWithTime -Text "Adding Files from subfolders" {
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
        }
        $LinkPrivatePublicFiles = $LinkPrivatePublicFiles | Select-Object -Unique

        if ($Configuration.Information.Manifest) {

            $Functions = Write-TextWithTime -Text 'Preparing functions to export' {
                Get-FunctionNamesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.FunctionsToExport
            }
            if ($Functions) {
                Write-TextWithTime -Text "Exported functions $Functions"
                $Configuration.Information.Manifest.FunctionsToExport = $Functions
            }

            $Aliases = Write-TextWithTime -Text 'Preparing aliases' {
                Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.AliasesToExport
            }
            if ($Aliases) {
                Write-TextWithTime -Text "Exported aliases $Aliases"
                $Configuration.Information.Manifest.AliasesToExport = $Aliases
            }

            if (-not [string]::IsNullOrWhiteSpace($Configuration.Information.ScriptsToProcess)) {
                $StartsWithEnums = "$($Configuration.Information.ScriptsToProcess)\"
                $FilesEnums = $LinkPrivatePublicFiles | Where-Object { ($_).StartsWith($StartsWithEnums) }

                if ($FilesEnums.Count -gt 0) {
                    #Write-Verbose "ScriptsToProcess export: $FilesEnums"
                    Write-TextWithTime -Text "ScriptsToProcess export $FilesEnums"
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

            #Write-Verbose '[+] Linking files from Root Dir'
            Write-TextWithTime -Text "Linking files from Root Dir" {
                Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            }
            Write-TextWithTime -Text "Linking files from Sub Dir" {
                #Write-Verbose '[+] Linking files from Sub Dir'
                Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            }

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
                -FormatCodePSD1 $Configuration.Options.Merge.FormatCodePSD1

        } else {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullModulePath\$Directory"
                Add-Directory $Dir
            }

            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            #Set-LinkedFiles -LinkFiles $LinkFilesSpecial -FullModulePath $PrivateProjectPath -FullProjectPath $AddPrivate -Delete
            #Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
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

    # Revers Path to current locatikon
    Set-Location -Path $CurrentLocation

    # Import Modules Section
    if ($Configuration) {

        if ($Configuration.Options.ImportModules.RequiredModules) {
            Write-TextWithTime -Text 'Importing modules - REQUIRED' {
                foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                    Import-Module -Name $Module -Force -ErrorAction Stop -Verbose:$false  #$Configuration.Options.ImportModules.Verbose
                }
            }
        }
        if ($Configuration.Options.ImportModules.Self) {
            Write-TextWithTime -Text 'Importing module - SELF' {
                Import-Module -Name $ProjectName -Force -ErrorAction Stop -Verbose:$false
            }
        }

        if ($Configuration.Steps.BuildDocumentation) {
            $DocumentationPath = "$FullProjectPath\$($Configuration.Options.Documentation.Path)"
            $ReadMePath = "$FullProjectPath\$($Configuration.Options.Documentation.PathReadme)"
            Write-Verbose "Generating documentation to $DocumentationPath with $ReadMePath"

            if (-not (Test-Path -Path $DocumentationPath)) {
                $null = New-Item -Path "$FullProjectPath\Docs" -ItemType Directory -Force
            }
            $Files = Get-ChildItem -Path $DocumentationPath
            if ($Files.Count -gt 0) {
                $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop
            } else {
                $null = New-MarkdownHelp -Module $ProjectName -WithModulePage -OutputFolder $DocumentationPath -ErrorAction Stop
                $null = Move-Item -Path "$DocumentationPath\$ProjectName.md" -Destination $ReadMePath
                #Start-Sleep -Seconds 1
                # this is temporary workaround - due to diff output on update
                if ($Configuration.Options.Documentation.UpdateWhenNew) {
                    $null = Update-MarkdownHelpModule $DocumentationPath -RefreshModulePage -ModulePagePath $ReadMePath -ErrorAction Stop
                }
                #
            }
        }
    }
}