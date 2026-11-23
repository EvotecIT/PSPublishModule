[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactPath,

    [Parameter(Mandatory)]
    [string] $Workload,

    [Parameter(Mandatory)]
    [ValidateSet('success', 'alternate')]
    [string] $Case
)

$ErrorActionPreference = 'Stop'
$env:PSModulePath = Join-Path $PSHOME 'Modules'
$root = $PWD.Path
$positive = $Case -eq 'success'
try {
    if ($Workload -eq 'PSSVG' -and $PSVersionTable.PSEdition -eq 'Desktop') {
        Add-Type -AssemblyName System.Web
    }
    $module = Import-Module -Name $ArtifactPath -PassThru -ErrorAction Stop
    'exports=' + ((@($module.ExportedCommands.Keys) | Sort-Object) -join ',')
    switch ($Workload) {
        'PSWriteHTML' {
            $text = if ($positive) { 'Fixture body' } else { 'Fixture body <script> & x' }
            $path = Join-Path $root 'report.html'
            New-HTML -TitleText 'Fixture report' -FilePath $path {
                New-HTMLText -Text $text
            }
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'Missing HTML report.' }
        }
        'PSWriteColor' {
            $color = if ($positive) { 'Green' } else { 'NeverAConsoleColor' }
            Write-Color -Text 'Fixture color' -Color $color -LogFile (Join-Path $root 'log.txt') -LogTime $false -OutputStream Output
        }
        'PSSharedGoods' {
            $country = if ($positive) { 'PL' } else { 'ZZ' }
            Convert-CountryCodeToCountry -CountryCode $country
        }
        'PSSVG' {
            $width = if ($positive) { 8 } else { 0 }
            $rectangle = SVG.rect -Width $width -Height 4
            $svg = (SVG.svg -Content $rectangle -Width 16 -Height 16).OuterXml
            if ($positive -and ($svg -isnot [string] -or [string]::IsNullOrWhiteSpace($svg))) {
                throw 'The SVG success case must produce serialized XML.'
            }
            $svg
        }
        'MarkdownPS' {
            $rows = @(
                [pscustomobject]@{Name = 'alpha'; Count = 2}
                [pscustomobject]@{Name = 'beta'; Count = 3}
            )
            if ($positive) { $rows | New-MDTable } else { $rows[0] | New-MDTable }
        }
        'PSStringScanner' {
            $text = if ($positive) { 'item=42' } else { 'item=X' }
            $scanner = New-PSStringScanner $text
            ConvertTo-Json -InputObject @($scanner.Scan('^[a-z]+'), $scanner.Scan('^='), $scanner.Scan('^\d+')) -Compress
        }
        'PScribo' {
            $format = if ($positive) { 'Html' } else { 'InvalidFixtureFormat' }
            Document -Name 'fixture' {
                Paragraph 'Fixture body'
            } | Export-Document -Path $root -Format $format
        }
        'BuildHelpers' {
            $project = Join-Path $root 'FixtureProject'
            $directory = Join-Path $project 'src'
            if ($positive) {
                New-Item -ItemType Directory -Path $directory -Force | Out-Null
                Set-Content -LiteralPath (Join-Path $directory 'FixtureModule.psd1') -Value "@{ ModuleVersion = '1.2.3' }"
            }
            Get-ProjectName -Path $project
        }
        'DSCParser' {
            $configuration = if ($positive) {
                "Configuration Fixture { Node localhost { File Demo { DestinationPath = 'C:\temp\fixture.txt'; Contents = 'fixture' } } }"
            } else { 'Configuration {' }
            $records = @(ConvertTo-DSCObject -Content $configuration -IncludeCIMInstanceInfo $false)
            if ($records.Count -ne 1 -or $records[0] -isnot [hashtable]) {
                throw 'The DSC fixture must produce one unordered property map.'
            }
            $record = $records[0]
            $fields = [ordered]@{}
            $keys = [string[]] @($record.Keys)
            [Array]::Sort($keys, [StringComparer]::Ordinal)
            foreach ($key in $keys) {
                $value = $record[$key]
                $type = if ($null -eq $value) { $null } else { $value.GetType().FullName }
                $fields[$key] = [ordered]@{ type = $type; value = $value }
            }
            ConvertTo-Json -InputObject ([ordered]@{ type = $record.GetType().FullName; fields = $fields }) -Depth 6 -Compress
        }
        'PSDocs' {
            $definition = "Document 'FixtureReport' { Title 'Fixture report'; Section 'Summary' { 'Fixture document body' } }"
            Set-Content -LiteralPath (Join-Path $root 'Fixture.Doc.ps1') -Value $definition -Encoding UTF8
            $subject = [pscustomobject]@{Name = 'Fixture item'}
            $path = if ($positive) { $root } else { Join-Path $root 'missing' }
            Invoke-PSDocument -Path $path -InputObject $subject -PassThru
        }
        default { throw "No approved module scenario for '$Workload'." }
    }
} catch {
    [Console]::Error.WriteLine($_.FullyQualifiedErrorId)
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
