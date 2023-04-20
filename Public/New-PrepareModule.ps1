function New-PrepareModule {
    <#
    .SYNOPSIS
    Short description

    .DESCRIPTION
    Long description

    .PARAMETER Settings
    Parameter description

    .PARAMETER Path
    Path to the folder where new project will be created. If not provided it will be created in one up folder from the location of build script.

    .PARAMETER ModuleName
    Module name to be used for the project.

    .PARAMETER FunctionsToExportFolder
    Parameter description

    .PARAMETER AliasesToExportFolder
    Parameter description

    .PARAMETER Configuration
    Parameter description

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [CmdletBinding()]
    param (
        [Parameter(Position = 0)][scriptblock] $Settings,
        [string] $Path,
        [alias('ProjectName')][string] $ModuleName,
        [string] $FunctionsToExportFolder = 'Public',
        [string] $AliasesToExportFolder = 'Public',
        [System.Collections.IDictionary] $Configuration = [ordered] @{},
        [string[]] $ExcludeFromPackage = @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'),
        [string[]] $IncludeRoot = @('*.psm1', '*.psd1', 'License*'),
        [string[]] $IncludePS1 = @('Private', 'Public', 'Enums', 'Classes'),
        [string[]] $IncludeAll = @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data'),
        [scriptblock] $IncludeCustomCode,
        [System.Collections.IDictionary] $IncludeToArray,
        [string] $LibrariesCore = 'Lib\Core',
        [string] $LibrariesDefault = 'Lib\Default',
        [string] $LibrariesStandard = 'Lib\Standard'
    )
    # this assumes that the script running this in Build or Publish folder (or any other folder that is 1 level below the root of the project)
    [string] $PathToProject = Get-Item -LiteralPath "$($MyInvocation.PSScriptRoot)/.."

    Write-Host "[i] Module Building Initializing..." -ForegroundColor Yellow
    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()

    New-PrepareStructure -Configuration $Configuration -Settings $Settings -PathToProject $PathToProject

    if ($Path -and $ModuleName) {
        if (-not (Test-Path -Path $Path)) {
            Write-Text "[-] Path $Path doesn't exists. This shouldn't be the case." -Color Red
        } else {
            $FullProjectPath = [io.path]::Combine($Path, $ModuleName)
            $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Build'
            Add-Directory -Directory $FullProjectPath
            foreach ($folder in $Folders) {
                $SubFolder = [io.path]::Combine($FullProjectPath, $Folder)
                Add-Directory -Directory $SubFolder
            }
            Copy-File -Source "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-ModuleStarter.txt" -Destination "$FullProjectPath\$ModuleName.psm1"
        }
    }
    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    Write-Host "[i] Module Building " -NoNewline -ForegroundColor Yellow
    Write-Host "[Time Total: $Execute]" -ForegroundColor Green
}