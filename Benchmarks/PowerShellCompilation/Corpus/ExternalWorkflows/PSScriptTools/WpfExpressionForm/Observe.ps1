param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Owned expression form probe requires STA'}
Add-Type -AssemblyName PresentationCore
[System.Windows.Media.RenderOptions]::ProcessRenderMode=[System.Windows.Interop.RenderMode]::SoftwareOnly
function global:Test-Expression { throw 'Expression command lookup escaped the in-memory provider' }
function global:Test-IsPSWindows { throw 'Platform command lookup escaped the fixture provider' }
$module=Import-Module $ModulePath -Force -PassThru
try {
 foreach($name in @('Test-Expression','Test-IsPSWindows')){
  $resolved=& $module {param($n) Get-Command $n -ErrorAction Stop} $name
  if($resolved.CommandType -ne 'Function' -or $resolved.ModuleName -ne $module.Name){throw "Authored $name command did not resolve to fixture provider"}
 }
 $result=@(Test-ExpressionForm)
 $captured=@(Get-CapturedExpression)
 if($captured.Count -ne 1 -or $captured[0].Expression -ne '40+2' -or $captured[0].Count -ne 1 -or
    -not $captured[0].IncludeExpression -or $captured[0].ArgumentCount -ne 0 -or $captured[0].Interval -ne 0.5){
  throw 'Run callback did not deliver expected arguments to the in-memory provider'
 }
 if($result.Count -ne 1 -or $result[0].Value -ne 'compiler-result' -or $result[0].Count -ne 1){throw 'Form return did not preserve provider result'}
 [ordered]@{
  hostVersion=$PSVersionTable.PSVersion.ToString()
  apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString()
  wpfAssembly=[Windows.Window].Assembly.FullName
  capturedCount=$captured.Count
  captured=$captured[0]
  resultCount=$result.Count
  result=$result[0]
 } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
}finally{
 Remove-Module $module -Force
 Remove-Item -LiteralPath Function:\Test-Expression -ErrorAction SilentlyContinue
 Remove-Item -LiteralPath Function:\Test-IsPSWindows -ErrorAction SilentlyContinue
}
