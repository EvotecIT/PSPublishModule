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
            $FullModulePath = "$modulePath\$projectName"
            $FullModulePathDelete = "$DeleteModulePath\$projectName"
            $FullTemporaryPath = [IO.path]::GetTempPath() + '' + $ProjectName
            $FullProjectPath = "$($Configuration.Path.Projects)\$($Configuration.Name)"
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


        if ($Configuration.Manifest) {

            $Configuration.Manifest.FunctionsToExport = ''
            $Configuration.Manifest.AliasesToExport = ''


            $Manifest = $Configuration.Manifest
            New-ModuleManifest @Manifest

            Write-Verbose "Converting $($Configuration.Manifest.Path)"
            (Get-Content $Configuration.Manifest.Path) | Out-FileUtf8NoBom $Configuration.Manifest.Path
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
        } else {
            foreach ($Directory in $LinkDirectories) {
                $Dir = "$FullModulePath\$Directory"
                Add-Directory $Dir
            }
        }



        if ($Configuration.Options.Merge.Use) {
            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullTemporaryPath -FullProjectPath $FullProjectPath


            Merge-Module -ModuleName $ProjectName `
            -ModulePathSource $FullTemporaryPath `
            -ModulePathTarget $FullModulePath `
            -Sort $Configuration.Options.Merge.Sort

        } else {
            Write-Verbose '[+] Linking files from Root Dir'
            Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            Write-Verbose '[+] Linking files from Sub Dir'
            Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
            #Set-LinkedFiles -LinkFiles $LinkFilesSpecial -FullModulePath $PrivateProjectPath -FullProjectPath $AddPrivate -Delete
            #Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        }

        if ($Configuration.Steps.Publish) {
            New-PublishModule -ProjectName $Configuration.Name -ApiKey $Configuration.PowershellGallery.ApiKey
        }
    }
    end {
        Set-Location -Path $CurrentLocation
    }
}