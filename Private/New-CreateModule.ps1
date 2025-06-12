function New-CreateModule {
    [CmdletBinding()]
    param (
        [string] $ProjectName,
        [string] $ModulePath,
        [string] $ProjectPath
    )
    $FullProjectPath = [io.path]::Combine($ProjectPath, $ProjectName)
    $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Publish', 'Enums', 'Data'
    Add-Directory -Directory $FullProjectPath
    foreach ($Folder in $Folders) {
        $PathToCreate = [io.path]::Combine($FullProjectPath, $Folder)
        Add-Directory -Directory $PathToCreate
    }
    $Source = [io.path]::Combine($PSScriptRoot, "..", 'Data', 'Example-Gitignore.txt')
    $Destination = [io.path]::Combine($FullProjectPath, '.gitignore')
    Copy-Item -Path $Source -Destination $Destination -ErrorAction Stop
    $Source = [io.path]::Combine($PSScriptRoot, "..", 'Data', 'Example-LicenseMIT.txt')
    $Destination = [io.path]::Combine($FullProjectPath, 'LICENSE')
    Copy-Item -Path $Source -Destination $Destination -ErrorAction Stop
    $Source = [io.path]::Combine($PSScriptRoot, "..", 'Data', 'Example-ModuleStarter.txt')
    $Destination = [io.path]::Combine($FullProjectPath, "$ProjectName.psm1")
    Copy-Item -Path $Source -Destination $Destination -ErrorAction Stop
}