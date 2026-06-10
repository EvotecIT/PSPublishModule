$script:ModuleRoot = $PSScriptRoot

foreach ($publicFunction in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Public') -Filter '*.ps1' -File -ErrorAction SilentlyContinue) {
    . $publicFunction.FullName
}

Export-ModuleMember -Function 'Get-PSPublishModuleArtefact', 'Install-PSPublishModuleArtefact'
