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

function ConvertToRuntimeDefaultValue([object]$value) {
  $tokens = [System.Collections.ArrayList]::new()
  AddRuntimeDefaultValueTokens $value $tokens
  return [ordered]@{
    tokens = @($tokens)
  }
}
