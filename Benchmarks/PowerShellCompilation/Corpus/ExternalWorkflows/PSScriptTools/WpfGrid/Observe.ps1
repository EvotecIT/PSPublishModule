param([Parameter(Mandatory)][string]$ModulePath,[Parameter(Mandatory)][string]$OutputPath,[Parameter(Mandatory)][string]$Title)
$ErrorActionPreference='Stop'
if([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA'){throw 'Owned WPF grid probe requires STA'}
Add-Type -AssemblyName PresentationCore
[System.Windows.Media.RenderOptions]::ProcessRenderMode=[System.Windows.Interop.RenderMode]::SoftwareOnly
$module=Import-Module $ModulePath -Force -PassThru
try {
 $before=@(Get-Job | Select-Object -ExpandProperty Id)
 $records=@(ConvertTo-WPFGrid -Title $Title -Scriptblock {
  [pscustomobject]@{Name='Alpha';Count='2'}
  [pscustomobject]@{Name='Beta';Count='5'}
 })
 if($records.Count -ne 0){throw 'Grid invocation unexpectedly wrote success output'}
 $job=@(Get-Job | Where-Object {$_.Id -notin $before})
 if($job.Count -ne 1){throw "Expected one owned cleanup job, saw $($job.Count)"}
 if(-not (Wait-Job -Job $job[0] -Timeout 75)){throw 'Grid cleanup job did not finish after Close'}
 if($job[0].State -ne 'Completed'){throw "Grid cleanup job ended $($job[0].State)"}
 $jobErrors=@($job[0].Error)
 if($jobErrors.Count -ne 0){throw "Grid cleanup job reported $($jobErrors.Count) error records"}
 if($job[0].Output.Count -ne 0 -or $job[0].Warning.Count -ne 0){throw 'Grid cleanup job emitted unexpected output or warnings'}
 [ordered]@{
  hostVersion=$PSVersionTable.PSVersion.ToString()
  apartment=[Threading.Thread]::CurrentThread.ApartmentState.ToString()
  wpfAssembly=[Windows.Window].Assembly.FullName
  jobState=$job[0].State.ToString()
  jobErrors=$jobErrors.Count
  resultCount=$records.Count
 } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
 Remove-Job -Job $job[0] -Force
}finally{Remove-Module $module -Force}
