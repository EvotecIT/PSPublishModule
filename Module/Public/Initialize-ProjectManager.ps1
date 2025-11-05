function Initialize-ProjectManager {
    <#
    .SYNOPSIS
    Builds VSCode Project manager config from filesystem

    .DESCRIPTION
    Builds VSCode Project manager config from filesystem

    .PARAMETER Path
    Path to where the projects are located

    .PARAMETER DisableSorting
    Disables sorting of the projects by last modified date

    .EXAMPLE
    Initialize-ProjectManager -Path "C:\Support\GitHub"

    .EXAMPLE
    Initialize-ProjectManager -Path "C:\Support\GitHub" -DisableSorting

    .NOTES
    General notes
    #>
    [cmdletBinding()]
    param(
        [parameter(Mandatory)][string] $Path,
        [switch] $DisableSorting
    )
    $ProjectsPath = Get-ChildItem -LiteralPath $Path -Directory

    $SortedProjects = foreach ($Project in $ProjectsPath) {
        $AllFiles = Get-ChildItem -LiteralPath $Project.FullName -Exclude ".\.git" -Recurse -Depth 2 -File
        $NewestFile = $AllFiles | Sort-Object -Descending -Property LastWriteTime | Select-Object -First 1

        [PSCustomObject] @{
            name          = $Project.name
            FullName      = $Project.FullName
            LastWriteTime = $NewestFile.LastWriteTime
        }

    }
    if (-not $DisableSorting) {
        $SortedProjects = $SortedProjects | Sort-Object -Descending -Property LastWriteTime
    }

    $ProjectManager = foreach ($Project in $SortedProjects) {
        [PSCustomObject] @{
            name     = $Project.name
            rootPath = $Project.FullName
            paths    = @()
            tags     = @()
            enabled  = $true
        }
    }
    $PathProjects = @(
        [io.path]::Combine($Env:APPDATA, "Code\User\globalStorage\alefragnani.project-manager")
        [io.path]::Combine($Env:APPDATA, "Cursor\User\globalStorage\alefragnani.project-manager")
        [io.path]::Combine($Env:APPDATA, "Cursor - Insiders\User\globalStorage\alefragnani.project-manager")
        [io.path]::Combine($Env:APPDATA, "Code - Insiders\User\globalStorage\alefragnani.project-manager")
        [io.path]::Combine($Env:APPDATA, "Windsurf\User\globalStorage\alefragnani.project-manager")
    )


    foreach ($Project in $PathProjects) {
        if (Test-Path -LiteralPath $Project) {
            $JsonPath = [io.path]::Combine($Project, 'projects.json')
            if (Test-Path -LiteralPath $JsonPath) {
                Get-Content -LiteralPath $JsonPath -Encoding UTF8 | Set-Content -LiteralPath "$JsonPath.backup"
            }
            $ProjectManager | ConvertTo-Json | Set-Content -LiteralPath $JsonPath
        }
    }
}