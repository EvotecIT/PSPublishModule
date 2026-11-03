@{
    RootModule = 'ModuleTestSample.psm1'
    ModuleVersion = '1.0.0'
    GUID = 'cba83688-a9a5-4cd7-a384-d58c607414a1'
    PowerShellVersion = '5.1'
    RequiredModules = @(@{ ModuleName = 'ModuleTestSampleDependency'; RequiredVersion = '1.0.0' })
    FunctionsToExport = @('Get-ModuleTestGreeting')
    CmdletsToExport = @()
    AliasesToExport = @()
}
