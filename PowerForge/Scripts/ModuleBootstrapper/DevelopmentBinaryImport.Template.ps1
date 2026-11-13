$PowerForgeDevelopmentLoadedType = '{{LibraryTypeName}}' -as [type]
$PowerForgeDevelopmentLoadedAssembly = if ($PowerForgeDevelopmentLoadedType -and $PowerForgeDevelopmentLoadedType.Assembly) { $PowerForgeDevelopmentLoadedType.Assembly } else { $null }
$PowerForgeDevelopmentLoadedAssemblyPath = if ($PowerForgeDevelopmentLoadedAssembly -and -not [string]::IsNullOrWhiteSpace($PowerForgeDevelopmentLoadedAssembly.Location)) { [IO.Path]::GetFullPath($PowerForgeDevelopmentLoadedAssembly.Location) } else { $null }
$PowerForgeDevelopmentSelectedBinaryPath = [IO.Path]::GetFullPath($PowerForgeDevelopmentBinaryPath)
$InnerModule = if ($PowerForgeDevelopmentLoadedAssembly -and [string]::Equals($PowerForgeDevelopmentLoadedAssemblyPath, $PowerForgeDevelopmentSelectedBinaryPath, [StringComparison]::OrdinalIgnoreCase)) {
    & $ImportModule -Assembly $PowerForgeDevelopmentLoadedType.Assembly -Force -PassThru -ErrorAction Stop
} else {
    & $ImportModule $PowerForgeDevelopmentBinaryPath -PassThru -ErrorAction Stop
}
if ($InnerModule) {
{{ImportedAliasesBlock}}
}
