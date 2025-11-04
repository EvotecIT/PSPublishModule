function Get-FunctionNames {
    [cmdletbinding()]
    param(
        [string] $Path,
        [switch] $Recurse
    )
    if ($Path -ne '' -and (Test-Path -LiteralPath $Path)) {
        $FilePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
        [System.Management.Automation.Language.Parser]::ParseFile(($FilePath),
            [ref]$null,
            [ref]$null).FindAll(
            { param($c)$c -is [Management.Automation.Language.FunctionDefinitionAst] }, $Recurse).Name
    }
}