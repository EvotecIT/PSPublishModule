function Get-ScriptsContent {
    <#
    .SYNOPSIS
    Gets script content and replaces $PSScriptRoot\..\..\ with $PSScriptRoot\

    .DESCRIPTION
    Gets script content and replaces $PSScriptRoot\..\..\ with $PSScriptRoot\

    .PARAMETER Files
    Parameter description

    .PARAMETER OutputPath
    Parameter description

    .EXAMPLE
    An example

    .NOTES
    Often in code people would use relative paths to get to the root of the module.
    This is all great but the path changes during merge.
    So we fix this by replacing $PSScriptRoot\..\..\ with $PSScriptRoot\
    While in best case they should always use $MyInvocation.MyCommand.Module.ModuleBase
    It's not always possible. So this is a workaround.

    $Content = @(
        '$PSScriptRoot\..\..\Build\Manage-PSWriteHTML.ps1'
        '$PSScriptRoot\..\Build\Manage-PSWriteHTML.ps1'
        '$PSScriptRoot\Build\Manage-PSWriteHTML.ps1'
        "[IO.Path]::Combine(`$PSScriptRoot, '..', 'Images')"
        "[IO.Path]::Combine(`$PSScriptRoot,'..','Images')"
    )
    $Content = $Content -replace [regex]::Escape('$PSScriptRoot\..\..\'), '$PSScriptRoot\' -replace [regex]::Escape('$PSScriptRoot\..\'), '$PSScriptRoot\'
    $Content = $Content -replace [regex]::Escape("`$PSScriptRoot, '..',"), '$PSScriptRoot,' -replace [regex]::Escape("`$PSScriptRoot,'..',"), '$PSScriptRoot,'
    $Content

    #>
    [cmdletbinding()]
    param(
        [string[]] $Files,
        [string] $OutputPath
    )

    foreach ($FilePath in $Files) {
        $Content = Get-Content -Path $FilePath -Raw -Encoding utf8
        if ($Content.Count -gt 0) {
            # $MyInvocation.MyCommand.Module.ModuleBase

            # Ensure file has content
            # $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
            # $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')
            #$Content = $Content -replace [regex]::Escape('$PSScriptRoot\..\..\'), '\$PSScriptRoot\\' -replace [regex]::Escape('$PSScriptRoot\..\'), '\$PSScriptRoot\'

            # Fixes [IO.Path]::Combine($PSScriptRoot, '..', 'Images') - mostly for PSTeams but should be useful for others
            #$Content = $Content.Replace("`$PSScriptRoot, '..',", "`$PSScriptRoot,")
            #$Content = $Content.Replace("`$PSScriptRoot,'..',", "`$PSScriptRoot,")
            #$Content = $Content -replace [regex]::Escape("`$PSScriptRoot, '..',"), "\`$PSScriptRoot," -replace [regex]::Escape("`$PSScriptRoot,'..',"), "\`$PSScriptRoot,"
            $Content = $Content -replace [regex]::Escape('$PSScriptRoot\..\..\'), '$PSScriptRoot\' -replace [regex]::Escape('$PSScriptRoot\..\'), '$PSScriptRoot\'
            $Content = $Content -replace [regex]::Escape("`$PSScriptRoot, '..',"), '$PSScriptRoot,' -replace [regex]::Escape("`$PSScriptRoot,'..',"), '$PSScriptRoot,'

            try {
                $Content | Out-File -Append -LiteralPath $OutputPath -Encoding utf8
            } catch {
                $ErrorMessage = $_.Exception.Message
                Write-Text "[-] Get-ScriptsContent - Merge on file $FilePath failed. Error: $ErrorMessage" -Color Red
                return $false
            }
        }
    }
}