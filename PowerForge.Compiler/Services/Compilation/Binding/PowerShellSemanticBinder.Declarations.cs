using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private static FunctionDeclaration[] DeclareFunctions(
        IEnumerable<ParsedSourceDocument> documents,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        PowerShellCompilationCapability capabilities)
    {
        var declarations = new List<FunctionDeclaration>();
        foreach (var document in documents)
        {
            foreach (var parseError in document.Errors)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB0001",
                    parseError.Message,
                    PowerShellSourceParser.GetSpan(document, parseError.Extent)));
            }
            if (document.Errors.Length > 0) continue;

            foreach (var declaration in document.RootFunctionDeclarations)
            {
                var original = declaration.Syntax;
                var conditional = declaration.UsesConditionalNativeBinding(capabilities);
                var function = conditional
                    ? CreateNativeFunctionSyntax(original, declaration.GetBindingName(document, capabilities))
                    : original;
                var span = PowerShellSourceParser.GetSpan(document, function.Extent);
                var owner = document;
                if (!conditional && capabilities == PowerShellCompilationCapabilities.HybridModule &&
                    document.TypeClosure.Declarations.Length > 0)
                {
                    var lexicalTypes = PowerShellModuleTypeLoadPolicy.FindLexicalTypeAnchors(document, original);
                    if (lexicalTypes.Count != 0)
                        owner = document.WithNativeDependencyTypes(document.NativeDependencyTypes.WithRuntimeLoadedTypes(lexicalTypes));
                }
                declarations.Add(new FunctionDeclaration(
                    owner,
                    function,
                    new PowerShellSymbolId(conditional ? PowerShellSymbolKind.ConditionalFunction : PowerShellSymbolKind.Function,
                        document.DocumentId, function.Name, span)));
            }
        }

        foreach (var duplicate in declarations.GroupBy(static declaration => declaration.Syntax.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            foreach (var declaration in duplicate)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB1002",
                    $"Function '{declaration.Syntax.Name}' is declared more than once under PowerShell's case-insensitive naming rules.",
                    declaration.Symbol.Declaration));
            }
        }
        foreach (var collision in declarations
                     .GroupBy(static declaration => PowerShellClrSymbolMapper.MapIdentifier(declaration.Syntax.Name), StringComparer.Ordinal)
                     .Where(static group => group.Select(declaration => declaration.Syntax.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
        {
            foreach (var declaration in collision)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    PowerShellCompilationFeatureIds.FunctionNameCollision,
                    $"Function '{declaration.Syntax.Name}' collides with another function on generated CLR method signature '{collision.Key}' after identifier normalization.",
                    declaration.Symbol.Declaration));
            }
        }
        return declarations.ToArray();
    }

    private sealed class FunctionDeclaration
    {
        internal FunctionDeclaration(ParsedSourceDocument document, FunctionDefinitionAst syntax, PowerShellSymbolId symbol,
            bool nativeNestedDeclaration = false)
        {
            Document = document;
            Syntax = syntax;
            Symbol = symbol;
            NativeNestedDeclaration = nativeNestedDeclaration;
        }

        internal ParsedSourceDocument Document { get; }
        internal FunctionDefinitionAst Syntax { get; }
        internal PowerShellSymbolId Symbol { get; }
        internal bool NativeNestedDeclaration { get; }
    }
}
