function Initialize-ProjectManager {
    <#
    .SYNOPSIS
    Builds VSCode Project manager config from filesystem

    .DESCRIPTION
    Builds VSCode Project manager config from filesystem

    .PARAMETER Path
    Path to where the projects are located

    .EXAMPLE
    Initialize-ProjectManager -Path "C:\Support\GitHub"

    .NOTES
    General notes
    #>
    [cmdletBinding()]
    param(
        [parameter(Mandatory)][string] $Path
    )
    $ProjectsPath = Get-ChildItem -LiteralPath $Path
    $ProjectManager = foreach ($_ in $ProjectsPath) {
        [PSCustomObject] @{
            name     = $_.name
            rootPath = $_.FullName
            paths    = @()
            group    = ''
            enabled  = $true
        }
    }
    $PathProjects = [io.path]::Combine($Env:APPDATA, "Code\User\globalStorage\alefragnani.project-manager"), [io.path]::Combine($Env:APPDATA, "Code\User\globalStorage\alefragnani.project-manager")

    foreach ($Project in $PathProjects) {
        if (Test-Path -LiteralPath $Project) {
            $JsonPath = [io.path]::Combine($Project, 'projects.json')
            if (Test-Path -LiteralPath $JsonPath) {
                Get-Content -LiteralPath $JsonPath | Set-Content -LiteralPath "$JsonPath.backup"
            }
            $ProjectManager | ConvertTo-Json | Set-Content -LiteralPath $JsonPath
        }
    }
}