function New-CreateModule {
    [CmdletBinding()]
    param (
        [string] $ProjectName,
        [string] $ModulePath,
        [string] $ProjectPath
    )
    $FullProjectPath = "$projectPath\$projectName"
    $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Publish', 'Enums', 'Data'
    Add-Directory $FullProjectPath
    foreach ($folder in $Folders) {
        Add-Directory "$FullProjectPath\$folder"
    }

    Copy-File -Source "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
    Copy-File -Source "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
    Copy-File -Source "$PSScriptRoot\..\Data\Example-ModuleStarter.txt" -Destination  "$FullProjectPath\$ProjectName.psm1"
}