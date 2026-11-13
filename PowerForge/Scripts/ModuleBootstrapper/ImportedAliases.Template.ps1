# The loader runs in its own scope, where Import-Module leaves the imported module's aliases.
# Define them on the module so its exports publish them.
foreach ($ImportedAlias in @($InnerModule.ExportedAliases.Values)) {
    Set-Alias -Name $ImportedAlias.Name -Value $ImportedAlias.Definition -Scope Script -Force
}
