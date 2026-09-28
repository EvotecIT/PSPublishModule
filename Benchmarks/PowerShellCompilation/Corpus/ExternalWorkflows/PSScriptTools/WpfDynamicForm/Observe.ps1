param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Owned WPF dynamic form probe requires STA'}
Add-Type -AssemblyName PresentationCore
[System.Windows.Media.RenderOptions]::ProcessRenderMode=[System.Windows.Interop.RenderMode]::SoftwareOnly
function global:Set-Clipboard { throw 'Unqualified clipboard command lookup escaped the in-memory provider' }
$module=Import-Module $ModulePath -Force -PassThru
try {
 $resolved=& $module {Get-Command Set-Clipboard -ErrorAction Stop}
 if($resolved.CommandType -ne 'Function' -or $resolved.ModuleName -ne $module.Name){throw 'Authored clipboard command did not resolve to the fixture provider'}
 $output=@(New-PSDynamicParameterForm)
 if($output.Count -ne 0){throw 'Form unexpectedly wrote success output'}
 $captured=Get-CapturedClipboard
 if($captured.Count -ne 1 -or $captured.Text -notmatch "RuntimeDefinedParameter\('CompilerProbe'" -or
    $captured.Text -notmatch 'If \(\$True\)'){
  throw 'Create callback did not deliver the expected dynamic-parameter code to the in-memory provider'
 }
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$hash=$sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($captured.Text))}finally{$sha.Dispose()}
 [ordered]@{
  hostVersion=$PSVersionTable.PSVersion.ToString()
  apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString()
  wpfAssembly=[Windows.Window].Assembly.FullName
  provider=$resolved.CommandType.ToString()
  capturedCount=$captured.Count
  codeLength=$captured.Text.Length
  codeSha256=([BitConverter]::ToString($hash).Replace('-','').ToLowerInvariant())
  resultCount=$output.Count
 } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}finally{
 Remove-Module $module -Force
 Remove-Item -LiteralPath Function:\Set-Clipboard -ErrorAction SilentlyContinue
}
