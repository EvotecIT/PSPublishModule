function Get-ScriptsContent {
    [cmdletbinding()]
    param(
        [string[]] $Files,
        [string] $OutputPath
    )

    foreach ($FilePath in $Files) {
        $Content = Get-Content -Path $FilePath -Raw -Encoding utf8
        if ($Content.Count -gt 0) {
            # Ensure file has content
            $Content = $Content.Replace('$PSScriptRoot\..\..\', '$PSScriptRoot\')
            $Content = $Content.Replace('$PSScriptRoot\..\', '$PSScriptRoot\')

            # Fixes [IO.Path]::Combine($PSScriptRoot, '..', 'Images') - mostly for PSTeams but should be useful for others
            $Content = $Content.Replace("`$PSScriptRoot, '..',", "`$PSScriptRoot,")
            $Content = $Content.Replace("`$PSScriptRoot,'..',", "`$PSScriptRoot,")

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