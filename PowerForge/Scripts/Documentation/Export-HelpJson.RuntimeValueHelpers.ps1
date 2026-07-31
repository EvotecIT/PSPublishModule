function ConvertToUtf16CodeUnits([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    if ($index -gt 0) { [void]$builder.Append(',') }
    [void]$builder.Append(
      ([int]$text[$index]).ToString([System.Globalization.CultureInfo]::InvariantCulture))
  }
  return $builder.ToString()
}
