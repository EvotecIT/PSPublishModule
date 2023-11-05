function New-PrepareManifest {
    [CmdletBinding()]
    param(
        [string] $ProjectName,
        [string] $ModulePath,
        [string] $ProjectPath,
        $FunctionToExport,
        [string] $ProjectUrl
    )
    $Location = [System.IO.Path]::Combine($projectPath, $ProjectName)
    Set-Location -Path $Location
    $manifest = @{
        Path              = ".\$ProjectName.psd1"
        RootModule        = "$ProjectName.psm1"
        Author            = 'Przemyslaw Klys'
        CompanyName       = 'Evotec'
        Copyright         = 'Evotec (c) 2011-2022. All rights reserved.'
        Description       = "Simple project"
        FunctionsToExport = $functionToExport
        CmdletsToExport   = ''
        VariablesToExport = ''
        AliasesToExport   = ''
        FileList          = "$ProjectName.psm1", "$ProjectName.psd1"
        HelpInfoURI       = $projectUrl
        ProjectUri        = $projectUrl
    }
    New-ModuleManifest @manifest
}