$InitializeType = "$([IO.Path]::GetFileNameWithoutExtension($ModuleAssemblyPath)).Initialize" -as [type]
$InnerModule = if ($InitializeType) {
    & $ImportModule -Force -Assembly $InitializeType.Assembly -PassThru
} else {
    & $ImportModule $ModuleAssemblyPath -PassThru -ErrorAction Stop
}
if ($InnerModule) {
{{ImportedAliasesBlock}}
}
