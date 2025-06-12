function Invoke-ModuleBuild {
    <#
    .SYNOPSIS
    Command to create new module or update existing one.
    It will create new module structure and everything around it, or update existing one.

    .DESCRIPTION
    Command to create new module or update existing one.
    It will create new module structure and everything around it, or update existing one.

    .PARAMETER Settings
    Provide settings for the module in form of scriptblock.
    It's using DSL to define settings for the module.

    .PARAMETER Path
    Path to the folder where new project will be created, or existing project will be updated.
    If not provided it will be created in one up folder from the location of build script.

    .PARAMETER ModuleName
    Provide name of the module. It's required parameter.

    .PARAMETER FunctionsToExportFolder
    Public functions folder name. Default is 'Public'.
    It will be used as part of PSD1 and PSM1 to export only functions from this folder.

    .PARAMETER AliasesToExportFolder
    Public aliases folder name. Default is 'Public'.
    It will be used as part of PSD1 and PSM1 to export only aliases from this folder.

    .PARAMETER Configuration
    Provides a way to configure module using hashtable.
    It's the old way of configuring module, that requires knowledge of inner workings of the module to name proper key/value pairs
    It's required for compatibility with older versions of the module.

    .PARAMETER ExcludeFromPackage
    Exclude files from Artefacts. Default is '.*, 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'.

    .PARAMETER IncludeRoot
    Include files in the Artefacts from root of the project. Default is '*.psm1', '*.psd1', 'License*' files.
    Other files will be ignored.

    .PARAMETER IncludePS1
    Include *.ps1 files in the Artefacts from given folders. Default are 'Private', 'Public', 'Enums', 'Classes' folders.
    If the folder doesn't exists it will be ignored.

    .PARAMETER IncludeAll
    Include all files in the Artefacts from given folders. Default are 'Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data' folders.

    .PARAMETER IncludeCustomCode
    Parameter description

    .PARAMETER IncludeToArray
    Parameter description

    .PARAMETER LibrariesCore
    Parameter description

    .PARAMETER LibrariesDefault
    Parameter description

    .PARAMETER LibrariesStandard
    Parameter description

    .PARAMETER ExitCode
    Exit code to be returned to the caller. If not provided, it will not exit the script, but finish gracefully.
    Exit code 0 means success, 1 means failure.

    .EXAMPLE
    An example

    .NOTES
    General notes
    #>
    [alias('New-PrepareModule', 'Build-Module', 'Invoke-ModuleBuilder')]
    [CmdletBinding(DefaultParameterSetName = 'Modern')]
    param (
        [Parameter(Position = 0, ParameterSetName = 'Modern')][scriptblock] $Settings,
        [parameter(ParameterSetName = 'Modern')][string] $Path,
        [parameter(Mandatory, ParameterSetName = 'Modern')][alias('ProjectName')][string] $ModuleName,
        [parameter(ParameterSetName = 'Modern')][string] $FunctionsToExportFolder = 'Public',
        [parameter(ParameterSetName = 'Modern')][string] $AliasesToExportFolder = 'Public',
        [Parameter(Mandatory, ParameterSetName = 'Configuration')][System.Collections.IDictionary] $Configuration = [ordered] @{},
        [parameter(ParameterSetName = 'Modern')][string[]] $ExcludeFromPackage = @('.*', 'Ignore', 'Examples', 'package.json', 'Publish', 'Docs'),
        [parameter(ParameterSetName = 'Modern')][string[]] $IncludeRoot = @('*.psm1', '*.psd1', 'License*'),
        [parameter(ParameterSetName = 'Modern')][string[]] $IncludePS1 = @('Private', 'Public', 'Enums', 'Classes'),
        [parameter(ParameterSetName = 'Modern')][string[]] $IncludeAll = @('Images', 'Resources', 'Templates', 'Bin', 'Lib', 'Data'),
        [parameter(ParameterSetName = 'Modern')][scriptblock] $IncludeCustomCode,
        [parameter(ParameterSetName = 'Modern')][System.Collections.IDictionary] $IncludeToArray,
        [parameter(ParameterSetName = 'Modern')][string] $LibrariesCore = [io.path]::Combine("Lib", "Core"),
        [parameter(ParameterSetName = 'Modern')][string] $LibrariesDefault = [io.path]::Combine("Lib", "Default"),
        [parameter(ParameterSetName = 'Modern')][string] $LibrariesStandard = [io.path]::Combine("Lib", "Standard"),
        [parameter(ParameterSetName = 'Configuration')]
        [parameter(ParameterSetName = 'Modern')]
        [switch] $ExitCode
    )
    if ($PsCmdlet.ParameterSetName -eq 'Configuration') {
        $ModuleName = $Configuration.Information.ModuleName
    }

    if ($Path) {
        # Path is given so we use it as is
        $FullProjectPath = [io.path]::Combine($Path, $ModuleName)
    } else {
        # this assumes that the script running this in Build or Publish folder (or any other folder that is 1 level below the root of the project)
        $ProjectPathToUse = [io.path]::Combine($MyInvocation.PSScriptRoot, "..")
        $FullProjectPath = Get-Item -LiteralPath $ProjectPathToUse
    }

    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()

    if ($Path -and $ModuleName) {
        $FullProjectPath = [io.path]::Combine($Path, $ModuleName)
        if (-not (Test-Path -Path $Path)) {
            Write-Text -Text "[-] Path $Path doesn't exists. Please create it, before continuing." -Color Red
            if ($ExitCode) {
                Exit 1
            } else {
                return
            }
        } else {
            $CopiedBuildModule = $false
            $CopiedPSD1 = $false
            if (Test-Path -Path $FullProjectPath) {
                Write-Text -Text "[i] Module $ModuleName ($FullProjectPath) already exists. Skipping inital steps" -Color DarkGray
            } else {
                Write-Text -Text "[i] Preparing module structure for $ModuleName in $Path" -Color DarkGray
                $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Build'
                Add-Directory -Directory $FullProjectPath
                foreach ($folder in $Folders) {
                    $SubFolder = [io.path]::Combine($FullProjectPath, $Folder)
                    Add-Directory -Directory $SubFolder
                }
                # this is a workaround for module before build and after build as the path changes
                $PathToData = [io.path]::Combine($PSScriptRoot, "..", "Data")
                if (-not (Test-Path -LiteralPath $PathToData)) {
                    $PathToData = [io.path]::Combine($PSScriptRoot, "Data")
                }
                $FilesToCopy = [ordered] @{
                    '.gitignore'       = @{
                        Source      = [io.path]::Combine($PathToData, "Example-Gitignore.txt")
                        Destination = [io.path]::Combine($FullProjectPath, ".gitignore")
                    }
                    'CHANGELOG.MD'     = @{
                        Source      = [io.path]::Combine($PathToData, "Example-CHANGELOG.MD")
                        Destination = [io.path]::Combine($FullProjectPath, "CHANGELOG.MD")
                    }
                    'README.MD'        = @{
                        Source      = [io.path]::Combine($PathToData, "Example-README.MD")
                        Destination = [io.path]::Combine($FullProjectPath, "README.MD")
                    }
                    'License'          = @{
                        Source      = [io.path]::Combine($PathToData, "Example-LicenseMIT.txt")
                        Destination = [io.path]::Combine($FullProjectPath, "LICENSE")
                    }
                    'Build-Module.ps1' = @{
                        Source      = [io.path]::Combine($PathToData, "Example-ModuleBuilder.txt")
                        Destination = [io.path]::Combine($FullProjectPath, "Build", "Build-Module.ps1")
                    }
                    "$ModuleName.psm1" = @{
                        Source      = [io.path]::Combine($PathToData, "Example-ModulePSM1.txt")
                        Destination = [io.path]::Combine($FullProjectPath, "$ModuleName.psm1")
                    }
                    "$ModuleName.psd1" = @{
                        Source      = [io.path]::Combine($PathToData, "Example-ModulePSD1.txt")
                        Destination = [io.path]::Combine($FullProjectPath, "$ModuleName.psd1")
                    }
                }
                foreach ($File in $FilesToCopy.Keys) {
                    $ValueToProcess = $FilesToCopy[$File]
                    $SourceFilePath = $ValueToProcess.Source
                    $DestinationFilePath = $ValueToProcess.Destination
                    if (-not (Test-Path -LiteralPath $DestinationFilePath)) {
                        Write-Text -Text "   [+] Copying '$($File)' file ($SourceFilePath)" -Color DarkGray
                        Copy-Item -Path $SourceFilePath -Destination $DestinationFilePath -ErrorAction Stop
                        if ($File -eq 'Build-Module.ps1') {
                            $CopiedBuildModule = $True
                        } elseif ($File -eq "$ModuleName.psd1") {
                            $CopiedPSD1 = $True
                        }
                    }
                }
                # lets update module builder to proper module name and guid
                $Guid = (New-Guid).Guid
                if ($CopiedBuildModule) {
                    $FilePath = [io.path]::Combine($FullProjectPath, "Build", "Build-Module.ps1")
                    $Success = Register-DataForInitialModule -FilePath $FilePath -ModuleName $ModuleName -Guid $Guid
                    if ($Success -eq $false) {
                        if ($ExitCode) { Exit 1 } else { return }
                    }
                }
                if ($CopiedPSD1) {
                    $FilePath = [io.path]::Combine($FullProjectPath, "$ModuleName.psd1")
                    $Success = Register-DataForInitialModule -FilePath $FilePath -ModuleName $ModuleName -Guid $Guid
                    if ($Success -eq $false) {
                        if ($ExitCode) { Exit 1 } else { return }
                    }
                }
                Write-Text -Text "[i] Preparing module structure for $ModuleName in $Path. Completed." -Color DarkGray
            }
        }
    }

    $newPrepareStructureSplat = [ordered] @{
        Configuration           = $Configuration
        Settings                = $Settings
        PathToProject           = $FullProjectPath
        ModuleName              = $ModuleName
        FunctionsToExportFolder = $FunctionsToExportFolder
        AliasesToExportFolder   = $AliasesToExportFolder
        ExcludeFromPackage      = $ExcludeFromPackage
        IncludeRoot             = $IncludeRoot
        IncludePS1              = $IncludePS1
        IncludeAll              = $IncludeAll
        IncludeCustomCode       = $IncludeCustomCode
        IncludeToArray          = $IncludeToArray
        LibrariesCore           = $LibrariesCore
        LibrariesDefault        = $LibrariesDefault
        LibrariesStandard       = $LibrariesStandard
    }

    $ModuleOutput = New-PrepareStructure @newPrepareStructureSplat

    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    if ($ModuleOutput -notcontains $false) {
        Write-Host "[i] Module Build Completed " -NoNewline -ForegroundColor Green
        Write-Host "[Time Total: $Execute]" -ForegroundColor Green
        if ($ExitCode) {
            Exit 0
        }
    } else {
        Write-Host "[i] Module Build Failed " -NoNewline -ForegroundColor Red
        Write-Host "[Time Total: $Execute]" -ForegroundColor Red
        if ($ExitCode) {
            Exit 1
        }
    }
}