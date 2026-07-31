function GetText([object]$obj) {
  if ($null -eq $obj) { return '' }
  if ($obj -is [string]) { return [string]$obj }
  try { if ($obj.PSObject -and $obj.PSObject.Properties['Text']) { return [string]$obj.Text } } catch {
    # best effort: Get-Help payload shapes vary across PowerShell versions and object types
  }
  try { return [string]$obj } catch { return '' }
}

function ConvertToUtf16CodeUnits([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    if ($index -gt 0) { [void]$builder.Append(',') }
    [void]$builder.Append(
      ([int]$text[$index]).ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }
  return $builder.ToString()
}

function AddRuntimeNumericDefaultValueToken(
  [object]$value,
  [System.Collections.IList]$tokens
) {
  if ($value -is [double]) {
    if ([double]::IsNaN($value)) {
      $tokens.Add([ordered]@{
        kind = 'DoubleBits'
        text = [System.BitConverter]::DoubleToInt64Bits($value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }) | Out-Null
      return $true
    }
    $text = $value.ToString('G17', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0 -and [System.BitConverter]::DoubleToInt64Bits($value) -lt 0) { $text = '-0' }
    $tokens.Add([ordered]@{ kind = 'Double'; text = $text }) | Out-Null
    return $true
  }
  if ($value -is [single]) {
    if ([single]::IsNaN($value)) {
      $tokens.Add([ordered]@{
        kind = 'SingleBits'
        text = [System.BitConverter]::ToInt32(
          [System.BitConverter]::GetBytes([single]$value), 0).ToString(
            [System.Globalization.CultureInfo]::InvariantCulture)
      }) | Out-Null
      return $true
    }
    $text = $value.ToString('G9', [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -eq 0) {
      $bits = [System.BitConverter]::ToInt32([System.BitConverter]::GetBytes([single]$value), 0)
      if ($bits -lt 0) { $text = '-0' }
    }
    $tokens.Add([ordered]@{ kind = 'Single'; text = $text }) | Out-Null
    return $true
  }
  if ($value -is [decimal]) {
    $bits = [System.Decimal]::GetBits($value)
    $tokens.Add([ordered]@{
      kind = 'DecimalBits'
      text = ($bits | ForEach-Object {
        $_.ToString([System.Globalization.CultureInfo]::InvariantCulture)
      }) -join ','
    }) | Out-Null
    return $true
  }
  return $false
}

function ConvertToRuntimeDefaultValue([object]$value) {
  $tokens = [System.Collections.ArrayList]::new()
  AddRuntimeDefaultValueTokens $value $tokens
  return [ordered]@{
    tokens = @($tokens)
  }
}
