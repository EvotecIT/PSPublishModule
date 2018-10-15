function New-PrepareModule {
    [CmdletBinding()]
    param (
        $projectName,
        $modulePath,
        $projectPath,
        $DeleteModulePath,
        $AdditionalModulePath
    )
    Begin {
        $FullModulePath = "$modulePath\$projectName"
        $FullProjectPath = "$projectPath\$projectName"
        $FullModulePathDelete = "$DeleteModulePath\$projectName"

        $CurrentLocation = (Get-Location).Path
        Set-Location -Path $FullProjectPath

        Remove-Directory $FullModulePathDelete
        Remove-Directory $FullModulePath
        Add-Directory $FullModulePath

        $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates'

        $LinkFiles = @()
        $LinkDirectories = @()
        $LinkPrivatePublicFiles = @()
        $LinkFilesSpecial = @()

    }
    Process {

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
        #return

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

        foreach ($directory in $LinkDirectories) {
            $dir = "$FullModulePath\$directory"
            Add-Directory $Dir

        }

        Write-Verbose '[+] Linking files from Root Dir'
        Set-LinkedFiles -LinkFiles $LinkFilesRoot -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        Write-Verbose '[+] Linking files from Sub Dir'
        Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
        #Set-LinkedFiles -LinkFiles $LinkFilesSpecial -FullModulePath $PrivateProjectPath -FullProjectPath $AddPrivate -Delete
        #Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
    }
    end {
        Set-Location -Path $CurrentLocation
    }
}