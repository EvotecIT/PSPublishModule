Describe 'Build-Module' {
    It 'Create New Module' {
        $ModuleName = 'NewTestModule123456'
        $Path = "$Env:TEMP\Junk"

        # lets remove junk first if it exists
        $FullModulePath = [io.Path]::Combine($Path, $ModuleName)
        if (Test-Path -Path $FullModulePath) {
            Remove-Item -Path $FullModulePath -Recurse -Force
        }
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeFalse

        # This deals with OneDrive redirection or similar
        $DirectoryModulesCore = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\PowerShell\Modules"
        $DirectoryModules = "$([Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments))\WindowsPowerShell\Modules"
        $Desktop = [IO.path]::Combine($DirectoryModules, $ModuleName)
        $Core = [IO.path]::Combine($DirectoryModulesCore, $ModuleName)

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

        # lets crete the path to folder as we create it deep in temp
        New-Item -Path $Path -ItemType Directory -Force | Out-Null

        # Lets build module
        Build-Module -Path $Path -ModuleName $ModuleName

        # lets see if module is created
        $Exists = Test-Path -Path $FullModulePath
        $Exists | Should -BeTrue

        # lets find if all files are copied
        $FilesRelative = "$ModuleName.psd1", "$ModuleName.psm1", "CHANGELOG.MD", ".gitignore", "LICENSE", "README.MD"
        foreach ($File in $FilesRelative) {
            $FilePath = [io.Path]::Combine($FullModulePath, $File)
            $Exists = Test-Path -Path $FilePath
            $Exists | Should -BeTrue

            $Item = Get-Item -Path $FilePath
            $Item.Length | Should -BeGreaterThan 0
        }
        $FilesFullPath = [io.path]::Combine($FullModulePath, "Build" , "Build-Module.ps1")
        foreach ($File in $FilesFullPath) {
            $Exists = Test-Path -Path $File
            $Exists | Should -BeTrue
        }
        $Directories = "Build", "Examples", "Ignore", "Private", 'Public'
        foreach ($Directory in $Directories) {
            $Exists = Test-Path -Path ([io.Path]::Combine($FullModulePath, $Directory)) -PathType Container
            $Exists | Should -BeTrue
        }
    }
}