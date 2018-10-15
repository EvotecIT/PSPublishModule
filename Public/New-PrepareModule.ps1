function New-PrepareModule {
    [CmdletBinding()]
    param (
        $projectName,
        $modulePath,
        $projectPath,
        $DeleteModulePath,
        $AdditionalModulePath
    )
    $FullModulePath = "$modulePath\$projectName"
    $FullProjectPath = "$projectPath\$projectName"
    $FullModulePathDelete = "$DeleteModulePath\$projectName"

    Remove-Directory $FullModulePathDelete
    Remove-Directory $FullModulePath
    Add-Directory $FullModulePath

    $DirectoryTypes = 'Public', 'Private', 'Lib', 'Bin', 'Enums', 'Images', 'Templates'

    $LinkFiles = @()
    $LinkDirectories = @()
    $LinkPrivatePublicFiles = @()
    $LinkFilesSpecial = @()
    $Directories = Get-ChildItem -Path $FullProjectPath -Directory
    foreach ($directory in $Directories) {
        if ($DirectoryTypes -contains $directory.Name) {
            $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
        }
    }
    $Files = Get-ChildItem -Path $FullProjectPath -File -Recurse
    <#
    foreach ($File in $Files) {
        $LinkPrivatePublicFiles += Add-FilesWithFolders -File $File -ProjectPath $FullProjectPath -FileType '.ps1' -Folders 'Private', 'Public', 'Enums'
        $LinkPrivatePublicFiles += Add-FilesWithFolders -File $File -ProjectPath $FullProjectPath -FileType '.psm1', '.psd1' -Folders ''
        $LinkPrivatePublicFiles += Add-FilesWithFolders -File $File -ProjectPath $FullProjectPath -FileType '.dll', '.md'  -Folders 'Lib'
        $LinkPrivatePublicFiles += Add-FilesWithFolders -File $File -ProjectPath $FullProjectPath -FileType -Folders 'Lib'

    }
#>



    #$Files.FullName
    foreach ($file in $Files) {
        switch -Wildcard ($file.Name) {
            '*.psd1' {
                #Write-Color $File -Color Red
                $LinkFiles += Add-ObjectTo -Object $File -Type 'Files List'
            }
            '*.psm1' {
                # Write-Color $File.FulllName -Color Red
                $LinkFiles += Add-ObjectTo -Object $File -Type 'Files List'
            }
            "*.dll" {
                $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Lib'
            }
            "*.exe" {
                $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Bin'
            }
            '*.ps1' {
                $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Private', 'Public', 'Enums'
            }
            'License*' {
                $LinkFiles += Add-ObjectTo -Object $File -Type 'Files List'
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

    <#
    $AddPrivate = "$AdditionalModulePath\Private"
    $PrivateProjectPath = "$FullProjectPath\Private"

    $FilesSupportive = Get-ChildItem -Path $AddPrivate -File -Recurse
    foreach ($file in $FilesSupportive) {
        switch -Wildcard ($file.Name) {
            '*.ps1' {
                $LinkFilesSpecial += Add-ObjectTo -Object $File -Type 'Files List'
            }

        }
    }

#>
    foreach ($directory in $LinkDirectories) {
        $dir = "$FullModulePath\$directory"
        Add-Directory $Dir

    }
    Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
    Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
    #Set-LinkedFiles -LinkFiles $LinkFilesSpecial -FullModulePath $PrivateProjectPath -FullProjectPath $AddPrivate -Delete
    #Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
}
