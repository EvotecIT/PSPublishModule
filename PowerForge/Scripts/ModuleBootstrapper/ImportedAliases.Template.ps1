# The loader runs in its own scope, where Import-Module leaves the imported module's aliases.
# Collect them; the module scope defines and exports them after the loader returns.
foreach ($ImportedAlias in @($InnerModule.ExportedAliases.Values)) {
    $script:PowerForgeImportedAliases.Add($ImportedAlias)
}
