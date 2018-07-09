function New-CreateModule {
    param (
        [string] $ProjectName,
        $ModulePath,
        $ProjectPath
    )
    $FullProjectPath = "$projectPath\$projectName"
    $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Publish', 'Enums', 'Data'
    Add-Directory $FullProjectPath
    foreach ($folder in $Folders) {
        Add-Directory "$FullProjectPath\$folder"
    }

    Copy-Item -Path "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
    Copy-Item -Path "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
    Copy-Item -Path "$PSScriptRoot\..\Data\Example-ModuleStarter.psm1" -Destination "$FullProjectPath\$ProjectName.psm1"
}

function New-PrepareManifest {
    param(
        $ProjectName,
        $modulePath,
        $projectPath,
        $functionToExport,
        $projectUrl
    )

    Set-Location "$projectPath\$ProjectName"
    $manifest = @{
        Path              = ".\$ProjectName.psd1"
        RootModule        = "$ProjectName.psm1"
        Author            = 'Przemyslaw Klys'
        CompanyName       = 'Evotec'
        Copyright         = 'Evotec (c) 2018. All rights reserved.'
        Description       = "Simple project"
        FunctionsToExport = $functionToExport
        CmdletsToExport   = ''
        VariablesToExport = ''
        AliasesToExport   = ''
        FileList          = "$ProjectName.psm1", "$ProjectName.psd1"
        HelpInfoURI       = $projectUrl
        ProjectUri        = $projectUrl
    }
    New-ModuleManifest @manifest
}
function New-PrepareModule ($projectName, $modulePath, $projectPath, $DeleteModulePath) {
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
    $Directories = Get-ChildItem -Path $FullProjectPath -Directory
    foreach ($directory in $Directories) {
        if ($DirectoryTypes -contains $directory.Name) {
            $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
        }
    }
    $Files = Get-ChildItem -Path $FullProjectPath -File -Recurse
    foreach ($file in $Files) {
        switch -Wildcard ($file.Name) {
            '*.psd1' {
                $LinkFiles += Add-ObjectTo -Object $File -Type 'Files List'
            }
            '*.psm1' {
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
        }
    }
    foreach ($directory in $LinkDirectories) {
        $dir = "$FullModulePath\$directory"
        Add-Directory $Dir

    }
    Set-LinkedFiles -LinkFiles $LinkFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
    Set-LinkedFiles -LinkFiles $LinkPrivatePublicFiles -FullModulePath $FullModulePath -FullProjectPath $FullProjectPath
}
function New-PublishModule($projectName, $apikey) {
    Publish-Module -Name $projectName -Repository PSGallery -NuGetApiKey $apikey -verbose
}