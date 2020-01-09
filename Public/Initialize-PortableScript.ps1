function Initialize-PortableScript {
    [cmdletBinding()]
    param(
        [uri] $FilePath,
        [uri] $OutputPath,
        [Array] $ApprovedModules
    )
    $Output = Get-MissingFunctions -FilePath $Script -SummaryWithCommands -ApprovedModules $ApprovedModules
    $Script = Get-Content -LiteralPath $Script
    $FinalScript = @(
        $Output.Functions
        $Script
    )
    $FinalScript | Set-Content -LiteralPath $OutputScript

    $Output
}