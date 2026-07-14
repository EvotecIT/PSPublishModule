param(
  [string]$RootPath,
  [string]$IncludeB64,
  [string]$ExcludeB64,
  [string]$Thumbprint,
  [string]$PfxPath,
  [string]$PfxBase64,
  [string]$PfxPassword,
  [string]$OverwriteSigned
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function DecodeLines([string]$b64) {
  if ([string]::IsNullOrWhiteSpace($b64)) { return @() }
  $text = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($b64))
  return $text -split "`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }
}

function EmitError([string]$msg) {
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes(([string]$msg)))
  Write-Output ('PFSIGN::ERROR::' + $b64)
}

function EmitSummary(
  [int]$totalMatched,
  [int]$totalAfterExclude,
  [int]$alreadyByThis,
  [int]$alreadyOther,
  [int]$attempted,
  [int]$signedNew,
  [int]$resigned,
  [int]$failed,
  [int]$unknownError,
  [string]$certThumbprint,
  [object[]]$failedFiles
) {
  $summary = [ordered]@{
    totalMatched            = $totalMatched
    totalAfterExclude       = $totalAfterExclude
    alreadySignedByThisCert = $alreadyByThis
    alreadySignedOther      = $alreadyOther
    attempted               = $attempted
    signedNew               = $signedNew
    resigned                = $resigned
    failed                  = $failed
    unknownError            = $unknownError
    certificateThumbprint   = $certThumbprint
    failedFiles             = @($failedFiles | Select-Object -First 25)
  }

  $json = $summary | ConvertTo-Json -Compress -Depth 6
  $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($json))
  Write-Output ('PFSIGN::SUMMARY::' + $b64)
  Write-Output ('PFSIGN::COUNT::' + [string]($signedNew + $resigned))
}

function Invoke-WithFileRetry {
  param(
    [Parameter(Mandatory = $true)] [scriptblock]$ScriptBlock,
    [Parameter(Mandatory = $true)] [string]$FilePath,
    [Parameter(Mandatory = $true)] [string]$Action
  )

  $maxAttempts = 15
  $delay = 200
  for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
    try {
      return & $ScriptBlock
    } catch {
      if ($attempt -ge $maxAttempts) {
        $message = $_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($message)) {
          $message = 'unknown error'
        }

        throw ('{0} failed for ''{1}'' after {2} attempt(s): {3}' -f $Action, $FilePath, $maxAttempts, $message)
      }

      Start-Sleep -Milliseconds $delay
      $delay = [Math]::Min($delay * 2, 5000)
    }
  }
}

function Add-FailedFile {
  param(
    [Parameter(Mandatory = $true)] [System.Collections.Generic.List[string]]$List,
    [Parameter(Mandatory = $true)] [string]$FilePath,
    [string]$Message
  )

  if ([string]::IsNullOrWhiteSpace($Message)) {
    $List.Add($FilePath) | Out-Null
    return
  }

  $List.Add(('{0} :: {1}' -f $FilePath, ($Message -replace '\s+', ' ').Trim())) | Out-Null
}

try {
  if ([string]::IsNullOrWhiteSpace($RootPath)) { throw "RootPath is required." }
  if (-not (Test-Path -LiteralPath $RootPath)) { throw "RootPath not found: $RootPath" }
  $root = (Resolve-Path -LiteralPath $RootPath).Path

  $include = DecodeLines $IncludeB64
  $exclude = DecodeLines $ExcludeB64
  $overwrite = -not [string]::IsNullOrWhiteSpace($OverwriteSigned) -and $OverwriteSigned -eq '1'

  if (-not $include -or $include.Count -eq 0) {
    # Keep defaults aligned with the C# pipeline (PowerForge.Services.ModulePipelineRunner).
    $include = @('*.ps1','*.psm1','*.psd1','*.dll','*.cat')
  }

  # Resolve certificate
  $cert = $null
  $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor `
           [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet

  if (-not [string]::IsNullOrWhiteSpace($PfxBase64)) {
    $bytes = [System.Convert]::FromBase64String($PfxBase64)
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes, $PfxPassword, $flags)
  } elseif (-not [string]::IsNullOrWhiteSpace($PfxPath)) {
    $pfxFull = (Resolve-Path -LiteralPath $PfxPath).Path
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxFull, $PfxPassword, $flags)
  } elseif (-not [string]::IsNullOrWhiteSpace($Thumbprint)) {
    $tp = ($Thumbprint -replace '\s','').ToUpperInvariant()
    $cert = Get-ChildItem -Path Cert:\CurrentUser\My -CodeSigningCert -ErrorAction SilentlyContinue |
      Where-Object { ($_.Thumbprint -replace '\s','').ToUpperInvariant() -eq $tp } |
      Select-Object -First 1
    if (-not $cert) {
      $cert = Get-ChildItem -Path Cert:\LocalMachine\My -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object { ($_.Thumbprint -replace '\s','').ToUpperInvariant() -eq $tp } |
        Select-Object -First 1
    }
  }

  if (-not $cert) {
    throw "Code signing certificate not configured or not found. Provide CertificateThumbprint/CertificatePFXPath/CertificatePFXBase64 (and password for PFX)."
  }

  $now = [DateTime]::UtcNow
  if ($now -lt $cert.NotBefore.ToUniversalTime() -or $now -gt $cert.NotAfter.ToUniversalTime()) {
    throw ("Code signing certificate '{0}' is outside its validity period ({1:u} through {2:u})." -f $cert.Thumbprint, $cert.NotBefore.ToUniversalTime(), $cert.NotAfter.ToUniversalTime())
  }

  $thisTp = ($cert.Thumbprint -replace '\s','').ToUpperInvariant()

  # Enumerate files (include patterns, then apply exclude substrings)
  $files = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
  foreach ($pat in $include) {
    if ([string]::IsNullOrWhiteSpace($pat)) { continue }
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter $pat -ErrorAction SilentlyContinue |
      ForEach-Object { [void]$files.Add($_.FullName) }
  }

  $all = @($files)
  if ($exclude -and $exclude.Count -gt 0) {
    $all = $all | Where-Object {
      $p = [string]$_
      foreach ($x in $exclude) {
        if ([string]::IsNullOrWhiteSpace($x)) { continue }
        if ($p.IndexOf($x, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $false }
      }
      return $true
    }
  }

  if (-not $all -or $all.Count -eq 0) {
    throw "No files matched for signing under '$root'."
  }

  $ts = 'http://timestamp.digicert.com'
  $alreadyByThis = 0
  $alreadyOther = 0
  $preStatus = @{}
  $attempted = 0
  $signedNew = 0
  $resigned = 0
  $failed = 0
  $unknownError = 0
  $failedFiles = New-Object 'System.Collections.Generic.List[string]'
  $precheckFailures = @{}

  if ($IsWindows -or $PSVersionTable.PSVersion.Major -le 5) {
    if (-not (Get-Command Get-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
      throw "Get-AuthenticodeSignature is not available."
    }
    if (-not (Get-Command Set-AuthenticodeSignature -ErrorAction SilentlyContinue)) {
      throw "Set-AuthenticodeSignature is not available."
    }

    foreach ($f in $all) {
      try {
        $sig = Invoke-WithFileRetry -FilePath $f -Action 'Get-AuthenticodeSignature' -ScriptBlock {
          Get-AuthenticodeSignature -FilePath $f -ErrorAction Stop
        }
        $status = [string]$sig.Status
        $preStatus[$f] = $status

        if ($status -ne 'NotSigned') {
          $tp = $sig.SignerCertificate?.Thumbprint
          if (-not [string]::IsNullOrWhiteSpace($tp)) { $tp = ($tp -replace '\s','').ToUpperInvariant() }
          if (-not [string]::IsNullOrWhiteSpace($tp) -and $tp -eq $thisTp) { $alreadyByThis++ } else { $alreadyOther++ }
        }
      } catch {
        $preStatus[$f] = 'PrecheckFailed'
        $precheckFailures[$f] = "precheck failed: " + $_.Exception.Message
      }
    }

    $targets = if ($overwrite) { $all } else { $all | Where-Object { $preStatus[$_] -eq 'NotSigned' } }
    $attempted = $targets.Count

    foreach ($f in $targets) {
      if ($precheckFailures.ContainsKey($f)) { [void]$precheckFailures.Remove($f) }
      $wasSigned = $preStatus[$f] -ne 'NotSigned'
      try {
        if ($overwrite) {
          $r = Invoke-WithFileRetry -FilePath $f -Action 'Set-AuthenticodeSignature' -ScriptBlock {
            Set-AuthenticodeSignature -FilePath $f -Certificate $cert -TimestampServer $ts -IncludeChain All -HashAlgorithm SHA256 -Force -ErrorAction Stop
          }
        } else {
          $r = Invoke-WithFileRetry -FilePath $f -Action 'Set-AuthenticodeSignature' -ScriptBlock {
            Set-AuthenticodeSignature -FilePath $f -Certificate $cert -TimestampServer $ts -IncludeChain All -HashAlgorithm SHA256 -ErrorAction Stop
          }
        }

        $status = if ($r) { [string]$r.Status } else { 'Unknown' }
        if ($status -eq 'UnknownError') { $unknownError++ }

        if ($status -eq 'Valid') {
          if ($wasSigned) { $resigned++ } else { $signedNew++ }
        } else {
          $failed++
          $statusMessage = if ($r) { [string]$r.StatusMessage } else { '' }
          Add-FailedFile -List $failedFiles -FilePath $f -Message ("signing returned status " + $status + " " + $statusMessage)
        }
      } catch {
        $failed++
        Add-FailedFile -List $failedFiles -FilePath $f -Message $_.Exception.Message
      }
    }

    foreach ($entry in $precheckFailures.GetEnumerator()) {
      $failed++
      Add-FailedFile -List $failedFiles -FilePath $entry.Key -Message $entry.Value
    }
  } else {
    if (-not (Get-Command Set-OpenAuthenticodeSignature -ErrorAction SilentlyContinue)) {
      throw "OpenAuthenticode is required on non-Windows for signing. Install the OpenAuthenticode module."
    }
    if (-not (Get-Command Get-OpenAuthenticodeSignature -ErrorAction SilentlyContinue)) {
      throw "OpenAuthenticode is required on non-Windows for signing. Install the OpenAuthenticode module."
    }

    foreach ($f in $all) {
      try {
        $sig = Invoke-WithFileRetry -FilePath $f -Action 'Get-OpenAuthenticodeSignature' -ScriptBlock {
          Get-OpenAuthenticodeSignature -FilePath $f -ErrorAction Stop
        }
        $status = [string]$sig.SStatus
        $preStatus[$f] = $status
        if ($status -ne 'NotSigned') { $alreadyOther++ }
      } catch {
        $preStatus[$f] = 'PrecheckFailed'
        $precheckFailures[$f] = "precheck failed: " + $_.Exception.Message
      }
    }

    $targets = if ($overwrite) { $all } else { $all | Where-Object { $preStatus[$_] -eq 'NotSigned' } }
    $attempted = $targets.Count

    foreach ($f in $targets) {
      if ($precheckFailures.ContainsKey($f)) { [void]$precheckFailures.Remove($f) }
      $wasSigned = $preStatus[$f] -ne 'NotSigned'
      try {
        if ($overwrite) {
          $r = Invoke-WithFileRetry -FilePath $f -Action 'Set-OpenAuthenticodeSignature' -ScriptBlock {
            Set-OpenAuthenticodeSignature -FilePath $f -Certificate $cert -TimeStampServer $ts -IncludeChain WholeChain -HashAlgorithm SHA256 -Force -ErrorAction Stop
          }
        } else {
          $r = Invoke-WithFileRetry -FilePath $f -Action 'Set-OpenAuthenticodeSignature' -ScriptBlock {
            Set-OpenAuthenticodeSignature -FilePath $f -Certificate $cert -TimeStampServer $ts -IncludeChain WholeChain -HashAlgorithm SHA256 -ErrorAction Stop
          }
        }

        $status = if ($r) { [string]$r.Status } else { 'Unknown' }
        if ($status -eq 'UnknownError') { $unknownError++ }

        if ($status -eq 'Valid') {
          if ($wasSigned) { $resigned++ } else { $signedNew++ }
        } else {
          $failed++
          $statusMessage = if ($r) { [string]$r.StatusMessage } else { '' }
          Add-FailedFile -List $failedFiles -FilePath $f -Message ("signing returned status " + $status + " " + $statusMessage)
        }
      } catch {
        $failed++
        Add-FailedFile -List $failedFiles -FilePath $f -Message $_.Exception.Message
      }
    }

    foreach ($entry in $precheckFailures.GetEnumerator()) {
      $failed++
      Add-FailedFile -List $failedFiles -FilePath $entry.Key -Message $entry.Value
    }
  }

  EmitSummary `
    -totalMatched @($files).Count `
    -totalAfterExclude @($all).Count `
    -alreadyByThis $alreadyByThis `
    -alreadyOther $alreadyOther `
    -attempted $attempted `
    -signedNew $signedNew `
    -resigned $resigned `
    -failed $failed `
    -unknownError $unknownError `
    -certThumbprint $thisTp `
    -failedFiles @($failedFiles)

  if ($failed -gt 0) { exit 2 } else { exit 0 }
} catch {
  EmitError $_.Exception.Message
  exit 1
}
