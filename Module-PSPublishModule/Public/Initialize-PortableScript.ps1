function Initialize-PortableScript {
    <#
    .SYNOPSIS
    Produces a self-contained script by inlining missing helper function definitions.

    .DESCRIPTION
    Analyzes the input script for function calls not present in the script itself, pulls helper
    definitions from approved modules, and writes a combined output file that begins with those
    helper definitions followed by the original script content. Useful for portable delivery.

    .PARAMETER FilePath
    Path to the source script to analyze and convert.

    .PARAMETER OutputPath
    Destination path for the generated self-contained script.

    .PARAMETER ApprovedModules
    Module names that are permitted sources for inlined helper functions.

    .EXAMPLE
    Initialize-PortableScript -FilePath .\Scripts\Do-Work.ps1 -OutputPath .\Artefacts\Do-Work.Portable.ps1 -ApprovedModules PSSharedGoods
    Generates a portable script with helper functions inlined at the top.

    .NOTES
    Output encoding is UTF8BOM on PS 7+, UTF8 on PS 5.1 for compatibility.
    #>
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
