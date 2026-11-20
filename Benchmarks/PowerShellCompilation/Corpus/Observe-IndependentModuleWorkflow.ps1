param([string] $ModulePath, [string] $ModuleName, [string] $DataRoot)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:PSModulePath = [IO.Path]::Combine($PSHOME, 'Modules')

Microsoft.PowerShell.Core\Import-Module Microsoft.PowerShell.Management, Microsoft.PowerShell.Utility
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
[Threading.Thread]::CurrentThread.CurrentUICulture = [Globalization.CultureInfo]::GetCultureInfo('en-US')
$module = Microsoft.PowerShell.Core\Import-Module -Name $ModulePath -PassThru -Force
if ($module.Name -ne $ModuleName) { throw "Unexpected module: $($module.Name)" }
switch ($ModuleName) {
    PSIni {
        $fixture = Join-Path $DataRoot 'settings.ini'
        $values = [ordered]@{ Server = [ordered]@{ Name='localhost'; Port='8080' }; Client = [ordered]@{ Enabled='true' } }
        $values | Export-Ini -LiteralPath $fixture -Force
        $actual = Import-Ini -LiteralPath $fixture
        if ($actual.Server.Port -ne '8080' -or $actual.Server.Name -ne 'localhost' -or $actual.Client.Enabled -ne 'true') { throw 'INI round trip failed.' }
        $failure = try { $null = Import-Ini -LiteralPath (Join-Path $DataRoot 'missing.ini') } catch { $_.FullyQualifiedErrorId }
        if (-not $failure) { throw 'Missing path was accepted.' }
        $result = [ordered]@{ values = $actual; text = [IO.File]::ReadAllText($fixture); errorId = $failure }
    }
    PSFolderSize {
        $null = [IO.Directory]::CreateDirectory((Join-Path $DataRoot 'one'))
        $null = [IO.Directory]::CreateDirectory((Join-Path $DataRoot 'two'))
        [IO.File]::WriteAllBytes((Join-Path $DataRoot 'one/a.bin'), [byte[]] @(1, 2, 3, 4))
        [IO.File]::WriteAllBytes((Join-Path $DataRoot 'one/b.bin'), [byte[]] @(1, 2))
        [IO.File]::WriteAllBytes((Join-Path $DataRoot 'two/a.bin'), [byte[]] @(1, 2, 3))
        $result = @(Get-FolderSize -BasePath $DataRoot -AddFileTotals | Sort-Object FolderName | ForEach-Object {
            [ordered]@{ name = $_.FolderName; bytes = $_.SizeBytes; files = $_.FileCount; type = $_.PSTypeNames[0] }
        })
        if ($result.Count -ne 2 -or $result[0].bytes -ne 6 -or $result[0].files -ne 2 -or
            $result[1].bytes -ne 3 -or $result[1].files -ne 1) { throw 'Directory inventory failed.' }
    }
    PowerShellHumanizer {
        $roman = @(4, 42 | ConvertTo-RomanNumeral)
        $integers = @('IV', 'XLII' | ConvertFrom-RomanNumeral)
        $ordinal = (21).Ordinalize()
        if (($roman -join ',') -cne 'IV,XLII' -or ($integers -join ',') -cne '4,42' -or $ordinal -cne '21st') { throw 'Humanizer conversion failed.' }
        $result = [ordered]@{ roman = $roman; integers = $integers; typeExtension = $ordinal }
    }
    Plaster {
        $template = [IO.Directory]::CreateDirectory((Join-Path $DataRoot 'template')).FullName
        $destination = Join-Path $DataRoot 'generated'
        $manifest = Join-Path $template 'plasterManifest.xml'
        $xml = @'
<?xml version="1.0" encoding="utf-8"?>
<plasterManifest schemaVersion="1.1" templateType="Project" xmlns="http://www.microsoft.com/schemas/PowerShell/Plaster/v1">
  <metadata>
    <name>CompilerFixture</name><id>6a165292-9266-4003-9233-3c680198b3f9</id><version>1.0.0</version>
    <title>Compiler Fixture</title><description>Generate a named text file.</description><author>PowerForge</author><tags>compiler</tags>
  </metadata>
  <parameters><parameter name="ProjectName" type="text" prompt="Project name" /></parameters>
  <content>
    <file source="content.txt" destination="${PLASTER_PARAM_ProjectName}.txt" />
  </content>
</plasterManifest>
'@
        [IO.File]::WriteAllText($manifest, $xml)
        [IO.File]::WriteAllText((Join-Path $template 'content.txt'), 'plaster fixture')
        $null = Test-PlasterManifest -Path $manifest
        $null = Invoke-Plaster -TemplatePath $template -DestinationPath $destination -ProjectName 'Qualified' 6>$null
        $files = @(Get-ChildItem -LiteralPath $destination -File)
        if ($files.Count -ne 1 -or $files[0].Name -cne 'Qualified.txt' -or [IO.File]::ReadAllText($files[0].FullName) -cne 'plaster fixture') {
            throw 'Plaster produced unexpected files.'
        }
        $result = [ordered]@{ name = $files[0].Name; text = [IO.File]::ReadAllText($files[0].FullName) }
    }
    default { throw "Unknown workflow '$ModuleName'." }
}
[ordered]@{ exported = @($module.ExportedCommands.Keys | Sort-Object); result = $result } | ConvertTo-Json -Compress -Depth 8
