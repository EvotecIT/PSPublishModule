namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string RenderStorage(PowerShellSymbolId symbol)
        => symbol.Kind == PowerShellSymbolKind.ModuleState
            ? "this.__moduleState." + PowerShellRuntimeFreeModuleSourceGenerator.FieldName(symbol)
            : PowerShellCSharpSymbolRenderer.Identifier(symbol.Name);
}
