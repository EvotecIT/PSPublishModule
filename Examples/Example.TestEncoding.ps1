Import-Module .\PSPublishModule.psd1 -Force

# Test encoding analysis
Write-Host "=== Encoding Analysis Demo ===" -ForegroundColor Cyan
$Summary = Get-ProjectEncoding -Path 'C:\Support\GitHub\PSPublishModule' -ProjectType PowerShell -GroupByEncoding -ShowFiles -ExcludeDirectories 'Artefacts'
$Summary | Format-List
$Summary.Files | Format-Table -AutoSize RelativePath, Extension, Encoding
$Summary.GroupedByEncoding | Format-List
$Summary.Summary | Format-List