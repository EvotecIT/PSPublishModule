@{
    RootModule = 'Generic.Compiler.Corpus.psm1'
    ModuleVersion = '1.0.0'
    GUID = '6a56ec5b-3484-41a6-b768-43f34e2b3105'
    Author = 'PowerForge'
    Description = 'Portable contract corpus for generic PowerShell typed compilation.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Measure-TextScore'
        'Get-CountdownValue'
        'Get-RuntimeState'
        'Get-CommandText'
        'Test-TokenPattern'
        'Get-EnvironmentBoundary'
        'Get-ObjectShape'
        'Get-CollectionShape'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
