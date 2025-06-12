Describe 'Build-Module' {
    It 'Create New Module' {
        $ModuleName = 'NewTestModule123456'
        $Path = Join-Path -Path $env:TEMP -ChildPath 'Junk'

        # lets remove junk first if it exists
        $FullModulePath = Join-Path -Path $Path -ChildPath $ModuleName
        if (Test-Path -Path $FullModulePath) {
            Remove-Item -Path $FullModulePath -Recurse -Force
        }
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        # Handle module paths for different operating systems
        if ($IsWindows) {
            $DirectoryModulesCore = Join-Path -Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) -ChildPath 'PowerShell' -AdditionalChildPath 'Modules'
            $DirectoryModules = Join-Path -Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)) -ChildPath 'WindowsPowerShell' -AdditionalChildPath 'Modules'
        } else {
            $DirectoryModulesCore = Join-Path -Path $env:HOME -ChildPath '.local' -AdditionalChildPath 'share' -AdditionalChildPath 'powershell' -AdditionalChildPath 'Modules'
            $DirectoryModules = $DirectoryModulesCore # On non-Windows, use the same path
        }

        $Desktop = Join-Path -Path $DirectoryModules -ChildPath $ModuleName
        $Core = Join-Path -Path $DirectoryModulesCore -ChildPath $ModuleName

        if (Test-Path -Path $Desktop) {
            Remove-Item -Path $Desktop -Recurse -Force
        }
        if (Test-Path -Path $Core) {
            Remove-Item -Path $Core -Recurse -Force
        }

        $Exists = Test-Path -Path $Desktop
        $Exists | Should -BeFalse

        $Exists = Test-Path -Path $Core
        $Exists | Should -BeFalse

        # lets create the path to folder as we create it deep in temp
        New-Item -Path $Path -ItemType Directory -Force | Out-Null

        # Lets build module
        Build-Module -Path $Path -ModuleName $ModuleName

        # lets see if module is created
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeTrue

        # lets find if all files are copied
        $FilesRelative = "$ModuleName.psd1", "$ModuleName.psm1", "CHANGELOG.MD", ".gitignore", "LICENSE", "README.MD"
        foreach ($File in $FilesRelative) {
            $FilePath = Join-Path -Path $FullModulePath -ChildPath $File
            $Exists = Test-Path -Path $FilePath
            $Exists | Should -BeTrue

            $Item = Get-Item -Path $FilePath
            $Item.Length | Should -BeGreaterThan 0
        }
        $FilesFullPath = Join-Path -Path $FullModulePath -ChildPath "Build" -AdditionalChildPath "Build-Module.ps1"
        foreach ($File in $FilesFullPath) {
            $Exists = Test-Path -Path $File
            $Exists | Should -BeTrue
        }
        $Directories = "Build", "Examples", "Ignore", "Private", 'Public'
        foreach ($Directory in $Directories) {
            $Exists = Test-Path -Path (Join-Path -Path $FullModulePath -ChildPath $Directory) -PathType Container
            $Exists | Should -BeTrue
        }
    }
}