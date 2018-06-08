function New-PrepareManifest($ProjectName, $modulePath, $projectPath, $functionToExport, $projectUrl) {
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
function New-PrepareModule ($projectName, $modulePath, $projectPath) {
    $FullModulePath = "$modulePath\$projectName"
    $FullProjectPath = "$projectPath\$projectName"

    Remove-Directory $FullModulePath
    Add-Directory $FullModulePath

    $LinkFiles = @()
    $LinkDirectories = @()
    $LinkPrivatePublicFiles = @()
    $Directories = Get-ChildItem -Path $FullProjectPath -Directory
    foreach ($directory in $Directories) {
        switch -Wildcard ($directory.Name) {
            'Public' {
                $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
            }
            'Private' {
                $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
            }
            'Lib' {
                $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
            }
            'Bin' {
                $LinkDirectories += Add-ObjectTo -Object $Directory -Type 'Directory List'
            }

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
                $LinkPrivatePublicFiles += Add-FilesWithFolders -file $file -FullProjectPath $FullProjectPath -directory 'Private', 'Public'
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