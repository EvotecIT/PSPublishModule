function New-PrepareModule {
    [CmdletBinding()]
    param (
        [string] $ProjectName,
        [string] $ProjectPath,
        [string] $ModulePath,
        [string] $DeleteModulePath,
        $Configuration
    )
    Begin {

        if ($Configuration) {
            $FullModulePath = [IO.path]::Combine($Configuration.Information.DirectoryModules,$Configuration.Information.ModuleName)
            $FullModulePathDelete = [IO.path]::Combine($Configuration.Information.DirectoryModules,$Configuration.Information.ModuleName)
            $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $Configuration.Information.ModuleName
            $FullProjectPath = [IO.Path]::Combine($Configuration.Information.DirectoryProjects, $Configuration.Information.ModuleName)
            $ProjectName = $Configuration.Information.ModuleName
        } else {
            $FullModulePath = "$modulePath\$projectName"
            $FullProjectPath = "$projectPath\$projectName"
            $FullModulePathDelete = "$DeleteModulePath\$projectName"
            $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $ProjectName
        }

        $CurrentLocation = (Get-Location).Path
        Set-Location -Path $FullProjectPath

        Remove-Directory $FullModulePathDelete
        Remove-Directory $FullModulePath
        Remove-Directory $FullTemporaryPath
        Add-Directory $FullModulePath
        Add-Directory $FullTemporaryPath

        $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates'

        $LinkFiles = @()
        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()
        $LinkFilesSpecial = @()

    }
    Process {


        if ($Configuration.Information.Manifest) {

            $Functions = Get-FunctionNamesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.FunctionsToExport
            if ($Functions) {
                $Configuration.Information.Manifest.FunctionsToExport = $Functions
            }
            $Aliases = Get-FunctionAliasesFromFolder -FullProjectPath $FullProjectPath -Folder $Configuration.Information.AliasesToExport
            if ($Aliases) {
                $Configuration.Information.Manifest.AliasesToExport = $Aliases
            }

            $Manifest = $Configuration.Information.Manifest
            New-ModuleManifest @Manifest
            #Update-ModuleManifest @Manifest

            if ($Configuration.Information.Versioning.Prerelease -ne '') {
                #$FilePathPSD1 = Get-Item -Path $Configuration.Information.Manifest.Path
                $Data = Import-PowerShellDataFile -Path $Configuration.Information.Manifest.Path
                $Data.PrivateData.PSData.Prerelease = $Configuration.Versioning.Prerelease
                $Data | Export-PSData -DataFile $Configuration.Information.Manifest.Path

            }

            Write-Verbose "Converting $($Configuration.Manifest.Path)"
            (Get-Content $Manifest.Path) | Out-FileUtf8NoBom $Manifest.Path
        }

        $Directories = Get-ChildItem -Path $FullProjectPath -Directory -Recurse
        foreach ($directory in $Directories) {
            $RelativeDirectoryPath = (Resolve-Path -LiteralPath $directory.FullName -Relative).Replace('.\', '')
            $RelativeDirectoryPath = "$RelativeDirectoryPath\"
            foreach ($LookupDir in $DirectoryTypes) {
                #Write-Verbose "New-PrepareModule - RelativeDirectoryPath: $RelativeDirectoryPath LookupDir: $LookupDir\"
                if ($RelativeDirectoryPath -like "$LookupDir\*" ) {
                    $LinkDirectories += Add-ObjectTo -Object $RelativeDirectoryPath -Type 'Directory List'
                }
            }
        }

        $Files = Get-ChildItem -Path $FullProjectPath -File -Recurse
        $AllFiles = @()
        foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $AllFiles += $RelativeFilePath
        }

        $RootFiles = @()
        $Files = Get-ChildItem -Path $FullProjectPath -File
        foreach ($File in $Files) {
            $RelativeFilePath = (Resolve-Path -LiteralPath $File.FullName -Relative).Replace('.\', '')
            $RootFiles += $RelativeFilePath
        }

        $LinkFilesRoot = @()
        # Link only files in Root Directory
        foreach ($File in $RootFiles) {
            switch -Wildcard ($file) {
                '*.psd1' {
                    #Write-Color $File -Color Red
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
                '*.psm1' {
                    # Write-Color $File.FulllName -Color Red
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
                'License*' {
                    $LinkFilesRoot += Add-ObjectTo -Object $File -Type 'Root Files List'
                }
            }
        }

        # Link only files from subfolers
        foreach ($file in $AllFiles) {
            switch -Wildcard ($file) {
                "*.dll" {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Lib'
                }
                "*.exe" {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Bin'
                }
                '*.ps1' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Private', 'Public', 'Enums'
                }
                '*license*' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Lib'
                }
                '*.jpg' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Images'
                }
                '*.png' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Images'
                }
                '*.xml' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Templates'
                }
                '*.docx' {
                    $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Templates'
                }
            }
        }

        if ($Configuration.Options.Merge.Use) {
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

            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath

            # Workaround to link files that are not ps1/psd1
            $FilesToLink = $LinkPrivatePublicFiles | Where-Object { $_ -notlike '*.ps1' -and $_ -notlike '*.psd1' }
            Set-LinkedFiles -LinkFiles $FilesToLink -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath


            Merge-Module -ModuleName $ProjectName `
            -ModulePathSource $FullTemporaryPath `
            -ModulePathTarget $FullModulePath `
            -Sort $Configuration.Options.Merge.Sort `
            -FunctionsToExport $Configuration.Information.Manifest.FunctionsToExport `
            -AliasesToExport $Configuration.Information.Manifest.AliasesToExport

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
        if ($Configuration.Publish.Use) {
            New-PublishModule -ProjectName $Configuration.Information.ModuleName -ApiKey $Configuration.Publish.ApiKey
        }
    }
    end {

        # Revers Path to current locatikon
        Set-Location -Path $CurrentLocation

        # Import Modules Section
        if ($Configuration) {
            if ($Configuration.Options.ImportModules.RequiredModules) {
                foreach ($Module in $Configuration.Information.Manifest.RequiredModules) {
                    Import-Module -Name $Module -Force
                }
            }
            if ($Configuration.Options.ImportModules.Self) {
                Import-Module -Name $ProjectName -Force
            }
        }
    }
}