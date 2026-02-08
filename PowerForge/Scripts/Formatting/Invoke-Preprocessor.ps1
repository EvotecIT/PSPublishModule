param([string]$FlagsB64,[Parameter(ValueFromRemainingArguments=$true)][string[]]$Files)
$ErrorActionPreference = 'Stop'

try {
  $flags = $null
  if ($FlagsB64) {
    $json = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($FlagsB64))
    $flags = ConvertFrom-Json -InputObject $json
  }
} catch { $flags = $null }

foreach ($f in $Files) {
  try {
    $text = Get-Content -LiteralPath $f -Raw -ErrorAction Stop
    $changed = $false

    if ($flags) {
      $tokens = $null; $errors = $null
      $ast = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
      $paramStart = $null; $paramEnd = $null
      if ($ast -and $ast.ParamBlock) { $paramStart = $ast.ParamBlock.Extent.StartOffset; $paramEnd = $ast.ParamBlock.Extent.EndOffset }

      if ($flags.RemoveCommentsBeforeParamBlock -and $paramStart -ne $null) {
        $ranges = @()
        foreach ($t in $tokens) {
          if ($t.Kind -eq 'Comment' -and $t.Extent.StartOffset -lt $paramStart) {
            $ranges += ,@($t.Extent.StartOffset, $t.Extent.EndOffset)
          }
        }
        foreach ($r in ($ranges | Sort-Object -Descending { $_[0] })) {
          $text = $text.Remove($r[0], $r[1]-$r[0])
          $changed = $true
        }
      }

      if ($flags.RemoveCommentsInParamBlock -and $paramStart -ne $null -and $paramEnd -ne $null) {
        $ranges = @()
        foreach ($t in $tokens) {
          if ($t.Kind -eq 'Comment' -and $t.Extent.StartOffset -ge $paramStart -and $t.Extent.EndOffset -le $paramEnd) {
            $ranges += ,@($t.Extent.StartOffset, $t.Extent.EndOffset)
          }
        }
        foreach ($r in ($ranges | Sort-Object -Descending { $_[0] })) {
          $text = $text.Remove($r[0], $r[1]-$r[0])
          $changed = $true
        }
      }
    }

    # Empty lines handling
    if ($flags -and ($flags.RemoveAllEmptyLines -or $flags.RemoveEmptyLines)) {
      $lines = @()
      $prevEmpty = $false
      foreach ($line in ($text -split "`r?`n")) {
        $isEmpty = ($line.Trim().Length -eq 0)
        if ($flags.RemoveAllEmptyLines) {
          if (-not $isEmpty) { $lines += $line }
        } elseif ($flags.RemoveEmptyLines) {
          if ($isEmpty) { if (-not $prevEmpty) { $lines += '' } } else { $lines += $line }
          $prevEmpty = $isEmpty
        }
      }
      $newText = ($lines -join "`r`n")
      if ($newText -ne $text) { $text = $newText; $changed = $true }
    }

    if ($changed) {
      [System.IO.File]::WriteAllText($f, $text, [System.Text.UTF8Encoding]::new($true))
      Write-Output ("PRE::CHANGED::" + $f)
    } else {
      Write-Output ("PRE::UNCHANGED::" + $f)
    }
  } catch {
    Write-Output ("PRE::ERROR::" + $f + "::" + $_.Exception.Message)
  }
}
exit 0
