param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath,[Parameter(Mandatory)][ValidateSet('message','input')][string]$Case,[Parameter(Mandatory)][string]$Title)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Owned WPF dialog probe requires STA'}
$module=Import-Module $ModulePath -Force -PassThru
$observations=[Collections.Generic.List[object]]::new()
try {
 if($Case -eq 'message'){$result=@(New-WPFMessageBox -Message 'Owned compiler dialog: Unicode Ω and wrapped text exercise rendering and the authored OK callback.' -Title $Title)}
 else{$result=@(Invoke-InputBox -Title $Title -Prompt 'Owned offline input: enter compiler-value')}
 if($result.Count -ne 1){throw 'Expected one callback result'}
 if($Case -eq 'message' -and ($result[0] -isnot [int] -or $result[0] -ne 1)){throw 'Message OK callback result mismatch'}
 if($Case -eq 'input' -and ($result[0] -isnot [string] -or $result[0] -ne 'compiler-value')){throw 'Input OK callback result mismatch'}
 $observations.Add([ordered]@{case=$Case;type=$result[0].GetType().FullName;value=$result[0];hostVersion=$PSVersionTable.PSVersion.ToString();apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString();wpfAssembly=[Windows.Window].Assembly.FullName})
}finally {Remove-Module $module -Force}
$observations.ToArray()|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $OutputPath -Encoding UTF8
