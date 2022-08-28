function New-PrepareModule {
    [CmdletBinding(DefaultParameterSetName = 'Existing')]
    param (
        [Parameter(ParameterSetName = 'New')][string] $Path,
        [Parameter(ParameterSetName = 'New')][string] $ProjectName,
        [Parameter(ParameterSetName = 'Existing')][System.Collections.IDictionary] $Configuration
    )
    Write-Host "[i] Module Building Initializing..." -ForegroundColor Yellow
    $GlobalTime = [System.Diagnostics.Stopwatch]::StartNew()
    if ($Configuration) {
        if (-not $Configuration.Information.DirectoryModulesCore) {
            #$Configuration.Information.DirectoryModulesCore = "$Env:USERPROFILE\Documents\PowerShell\Modules"
            $Configuration.Information.DirectoryModulesCore = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\PowerShell\Modules"
        }
        if (-not $Configuration.Information.DirectoryModules) {
            #$Configuration.Information.DirectoryModules = "$Env:USERPROFILE\Documents\WindowsPowerShell\Modules"
            $Configuration.Information.DirectoryModules = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\WindowsPowerShell\Modules"
        }
        # We build module or do other stuff with it
        if ($Configuration.Steps.BuildModule.Enable -or
            $Configuration.Steps.BuildModule.EnableDesktop -or
            $Configuration.Steps.BuildModule.EnableCore -or
            $Configuration.Steps.BuildDocumentation -eq $true -or
            $Configuration.Steps.BuildLibraries.Enable -or
            $Configuration.Steps.PublishModule.Enable -or
            $Configuration.Steps.PublishModule.Enabled) {
            Start-ModuleBuilding -Configuration $Configuration
        }
    }
    if ($Path -and $ProjectName) {
        if (-not (Test-Path -Path $Path)) {
            Write-Text "[-] Path $Path doesn't exists. This shouldn't be the case." -Color Red
        } else {
            $FullProjectPath = [io.path]::Combine($Path, $ProjectName)
            $Folders = 'Private', 'Public', 'Examples', 'Ignore', 'Publish', 'Enums', 'Data', 'Classes'
            Add-Directory -Directory $FullProjectPath
            foreach ($folder in $Folders) {
                $SubFolder = [io.path]::Combine($FullProjectPath, $Folder)
                Add-Directory -Directory $SubFolder
            }
            Copy-File -Source "$PSScriptRoot\..\Data\Example-Gitignore.txt" -Destination "$FullProjectPath\.gitignore"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-LicenseMIT.txt" -Destination "$FullProjectPath\License"
            Copy-File -Source "$PSScriptRoot\..\Data\Example-ModuleStarter.txt" -Destination "$FullProjectPath\$ProjectName.psm1"
        }
    }
    $Execute = "$($GlobalTime.Elapsed.Days) days, $($GlobalTime.Elapsed.Hours) hours, $($GlobalTime.Elapsed.Minutes) minutes, $($GlobalTime.Elapsed.Seconds) seconds, $($GlobalTime.Elapsed.Milliseconds) milliseconds"
    Write-Host "[i] Module Building " -NoNewline -ForegroundColor Yellow
    Write-Host "[Time Total: $Execute]" -ForegroundColor Green
}