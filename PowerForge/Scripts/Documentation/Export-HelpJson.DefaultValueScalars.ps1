function ConvertScalarToPowerShellDefaultValue([object]$value) {
  $scalarText = if ($value -is [System.IFormattable]) {
    ([System.IFormattable]$value).ToString($null, [System.Globalization.CultureInfo]::InvariantCulture)
  } else {
    ''
  }
  switch ($value.GetType().FullName) {
    'System.SByte' { return ('([System.SByte]' + $scalarText + ')') }
    'System.Byte' { return ('([System.Byte]' + $scalarText + ')') }
    'System.Int16' { return ('([System.Int16]' + $scalarText + ')') }
    'System.UInt16' { return ('([System.UInt16]' + $scalarText + ')') }
    'System.Int32' { return $scalarText }
    'System.UInt32' { return ('([System.UInt32]' + $scalarText + ')') }
    'System.Int64' { return ('([System.Int64]' + $scalarText + ')') }
    'System.UInt64' { return ('([System.UInt64]' + $scalarText + ')') }
    'System.IntPtr' { return ('[System.IntPtr]::new(([System.Int64]' + $scalarText + '))') }
    'System.UIntPtr' { return ('[System.UIntPtr]::new(([System.UInt64]' + $scalarText + '))') }
  }
  throw ('Unsupported PSDefaultValue runtime type: ' + $value.GetType().FullName)
}

function ConvertToXmlSafeDefaultHelpText([string]$text) {
  $builder = [System.Text.StringBuilder]::new()
  for ($index = 0; $index -lt $text.Length; $index++) {
    $character = $text[$index]
    if ([char]::IsHighSurrogate($character) -and
        $index + 1 -lt $text.Length -and
        [char]::IsLowSurrogate($text[$index + 1])) {
      [void]$builder.Append($character)
      [void]$builder.Append($text[++$index])
    } elseif ([System.Xml.XmlConvert]::IsXmlChar($character)) {
      [void]$builder.Append($character)
    } else {
      [void]$builder.Append('([char]' + [int]$character + ')')
    }
  }
  return $builder.ToString()
}
