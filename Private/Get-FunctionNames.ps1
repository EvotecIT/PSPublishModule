function Get-FunctionNames {
    [cmdletbinding()]
    param(
        [string] $Path,
        [switch] $Recurse
    )
    [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Path),
        [ref]$null,
        [ref]$null).FindAll(
        { param($c)$c -is [Management.Automation.Language.FunctionDefinitionAst] }, $Recurse).Name
}