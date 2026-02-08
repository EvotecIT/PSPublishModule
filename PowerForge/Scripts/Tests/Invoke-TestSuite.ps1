param(
  [string]$TestPath,
  [string]$OutputFormat,
  [string]$EnableCodeCoverage,
  [string]$CoverageProjectRoot,
  [string]$ResultsPath,
  [string]$ModuleName,
  [string]$ModuleImportPath,
  [string]$SkipImport,
  [string]$ForceImport,
  [string]$ImportModulesB64,
  [string]$ImportVerbose
)

function Encode([string]$s) {
  if ([string]::IsNullOrWhiteSpace($s)) { return '' }
  return [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($s))
}

function DecodeModules([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64))
  if ([string]::IsNullOrWhiteSpace($json)) { return @() }
  try { return $json | ConvertFrom-Json } catch { return @() }
}

try {
  Import-Module -Name Pester -Force -ErrorAction Stop
  $p = Get-Module -Name Pester
  if ($p -and $p.Version) {
    Write-Output ('PFTEST::PESTER::' + $p.Version.ToString())
  }

  $importVerbose = ($ImportVerbose -eq '1')
  $importModules = DecodeModules $ImportModulesB64
  if ($importModules) {
    foreach ($m in $importModules) {
      if (-not $m -or [string]::IsNullOrWhiteSpace($m.Name)) { continue }
      if ($m.RequiredVersion) {
        Import-Module -Name $m.Name -RequiredVersion $m.RequiredVersion -Force -ErrorAction Stop -Verbose:$importVerbose
      } elseif ($m.MinimumVersion) {
        Import-Module -Name $m.Name -MinimumVersion $m.MinimumVersion -Force -ErrorAction Stop -Verbose:$importVerbose
      } else {
        Import-Module -Name $m.Name -Force -ErrorAction Stop -Verbose:$importVerbose
      }
    }
  }

  $doImport = ($SkipImport -ne '1') -and (-not [string]::IsNullOrWhiteSpace($ModuleImportPath))
  if ($doImport) {
    Import-Module -Name $ModuleImportPath -Force:($ForceImport -eq '1') -ErrorAction Stop -Verbose:$importVerbose | Out-Null
    Write-Output 'PFTEST::IMPORT::OK'
    try {
      $m = Get-Module -Name $ModuleName | Select-Object -First 1
      if ($m) {
        $funcCount = 0
        $cmdletCount = 0
        $aliasCount = 0
        try { $funcCount = ($m.ExportedFunctions.Keys | Measure-Object).Count } catch { }
        try { $cmdletCount = ($m.ExportedCmdlets.Keys | Measure-Object).Count } catch { }
        try { $aliasCount = ($m.ExportedAliases.Keys | Measure-Object).Count } catch { }
        Write-Output ('PFTEST::EXPORTS::' + $funcCount + '::' + $cmdletCount + '::' + $aliasCount)
      }
    } catch { }
  }

  $enableCoverage = ($EnableCodeCoverage -eq '1')
  $useCoveragePath = $enableCoverage -and (-not [string]::IsNullOrWhiteSpace($CoverageProjectRoot))

  $r = $null
  if ($p -and $p.Version -and $p.Version.Major -ge 5) {
    $Configuration = [PesterConfiguration]::Default
    $Configuration.Run.Path = $TestPath
    $Configuration.Run.Exit = $false
    $Configuration.Run.PassThru = $true
    $Configuration.Should.ErrorAction = 'Continue'

    $Configuration.TestResult.Enabled = $true
    $Configuration.TestResult.OutputPath = $ResultsPath
    $Configuration.TestResult.OutputFormat = 'NUnitXml'

    $Configuration.CodeCoverage.Enabled = $enableCoverage
    if ($useCoveragePath) {
      $files = Get-ChildItem -Path $CoverageProjectRoot -Filter '*.ps1' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
      if ($files) { $Configuration.CodeCoverage.Path = $files.FullName }
    }

    switch ($OutputFormat) {
      'Detailed' { $Configuration.Output.Verbosity = 'Detailed' }
      'Normal'   { $Configuration.Output.Verbosity = 'Normal' }
      'Minimal'  { $Configuration.Output.Verbosity = 'Minimal' }
    }

    $r = Invoke-Pester -Configuration $Configuration
  } else {
    $PesterParams = @{
      Script      = $TestPath
      PassThru    = $true
      OutputFormat = 'NUnitXml'
      OutputFile  = $ResultsPath
      Verbose     = ($OutputFormat -eq 'Detailed')
    }

    if ($useCoveragePath) {
      $files = Get-ChildItem -Path $CoverageProjectRoot -Filter '*.ps1' -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Directory.Name -in @('Public', 'Private') }
      if ($files) { $PesterParams.CodeCoverage = $files.FullName }
    }

    $r = Invoke-Pester @PesterParams
  }

  if (-not $r) { throw 'Invoke-Pester returned no results' }

  $total = $r.TotalCount
  $passed = $r.PassedCount
  $failed = $r.FailedCount
  $skipped = $r.SkippedCount

  if ($null -eq $total -or $null -eq $passed -or $null -eq $failed) {
    $total = 0; $passed = 0; $failed = 0; $skipped = 0
    foreach ($t in $r.Tests) {
      $total++
      $res = $t.Result
      if ($res -eq 'Passed') { $passed++ }
      elseif ($res -eq 'Failed') { $failed++ }
      elseif ($res -eq 'Skipped') { $skipped++ }
    }
  }

  Write-Output ('PFTEST::COUNTS::' + $total + '::' + $passed + '::' + $failed + '::' + $skipped)

  try {
    if ($r.Time) { Write-Output ('PFTEST::DURATION::' + $r.Time.ToString()) }
  } catch { }

  try {
    if ($r.CodeCoverage) {
      $exec = [double]$r.CodeCoverage.NumberOfCommandsExecuted
      $an = [double]$r.CodeCoverage.NumberOfCommandsAnalyzed
      if ($an -gt 0) {
        $pct = [Math]::Round(($exec / $an) * 100.0, 2)
        Write-Output ('PFTEST::COVERAGE::' + $pct.ToString('0.00', [CultureInfo]::InvariantCulture))
      }
    }
  } catch { }

  exit 0
} catch {
  $m = $_.Exception.Message
  if ([string]::IsNullOrWhiteSpace($m)) { $m = $_.ToString() }
  Write-Output ('PFTEST::ERROR::' + (Encode $m))
  exit 2
}
