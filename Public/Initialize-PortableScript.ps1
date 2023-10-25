function Initialize-PortableScript {
    [cmdletBinding()]
    param(
        [string] $FilePath,
        [string] $OutputPath,
        [Array] $ApprovedModules
    )

    if ($PSVersionTable.PSVersion.Major -gt 5) {
        $Encoding = 'UTF8BOM'
    } else {
        $Encoding = 'UTF8'
    }

    $Output = Get-MissingFunctions -FilePath $FilePath -SummaryWithCommands -ApprovedModules $ApprovedModules
    $Script = Get-Content -LiteralPath $FilePath -Encoding UTF8
    $FinalScript = @(
        $Output.Functions
        $Script
    )
    $FinalScript | Set-Content -LiteralPath $OutputPath -Encoding $Encoding
    $Output
}