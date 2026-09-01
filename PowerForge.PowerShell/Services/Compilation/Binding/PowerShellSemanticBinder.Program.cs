using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    internal PowerShellBoundProgram Bind(
        IEnumerable<ParsedSourceDocument> documents,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        var orderedDocuments = documents.OrderBy(static item => item.DocumentId, StringComparer.Ordinal).ToArray();
        var diagnostics = new List<PowerShellSemanticDiagnostic>();
        var declarations = DeclareFunctions(orderedDocuments, diagnostics);
        var functionsByName = declarations
            .GroupBy(static declaration => declaration.Syntax.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .Where(group => HasTypedFunctionShape(group.Single().Syntax.Body, capabilities))
            .ToDictionary(
                static group => group.Key,
                group => PowerShellLocalCallSemanticBinder.CreateSignature(group.Single().Document, group.Single().Syntax, group.Single().Symbol, targetFramework, capabilities),
                StringComparer.OrdinalIgnoreCase);
        for (var iteration = 0; iteration < functionsByName.Count; iteration++)
        {
            var changed = false;
            foreach (var declaration in declarations)
            {
                if (!functionsByName.TryGetValue(declaration.Syntax.Name, out var signature) || signature.DeclaredReturnType is not null)
                    continue;
                var inferred = PowerShellLocalCallSemanticBinder.InferReturnType(declaration.Syntax, signature.Parameters, functionsByName);
                if (inferred is not null) changed |= signature.RefineReturnType(inferred);
            }
            if (!changed) break;
        }
        foreach (var declaration in declarations)
        {
            if (!functionsByName.TryGetValue(declaration.Syntax.Name, out var signature) ||
                !signature.IsPipelineLifecycle)
                continue;
            if (PowerShellLocalCallSemanticBinder.HasPipelineLifecycleProcessOutput(
                    declaration.Syntax,
                    signature.Parameters,
                    functionsByName))
                signature.SetPipelineLifecycleReturnsCollection();
        }

        var failedDiagnostics = new Dictionary<string, PowerShellSemanticDiagnostic[]>(StringComparer.OrdinalIgnoreCase);
        List<PowerShellBoundFunction> functions;
        while (true)
        {
            functions = new List<PowerShellBoundFunction>();
            var roundDiagnostics = new List<PowerShellSemanticDiagnostic>();
            foreach (var declaration in declarations.OrderBy(static item => item.Symbol.StableKey, StringComparer.Ordinal))
            {
                if (declaration.Document.Errors.Length > 0 || !functionsByName.ContainsKey(declaration.Syntax.Name)) continue;
                var functionDiagnostics = new List<PowerShellSemanticDiagnostic>();
                var bound = BindFunction(
                    declaration.Document,
                    declaration.Syntax,
                    declaration.Symbol,
                    functionsByName,
                    functionDiagnostics,
                    targetFramework,
                    capabilities);
                if (bound is not null)
                {
                    functions.Add(bound);
                    roundDiagnostics.AddRange(functionDiagnostics);
                }
                else if (!failedDiagnostics.ContainsKey(declaration.Syntax.Name))
                {
                    failedDiagnostics.Add(declaration.Syntax.Name, functionDiagnostics.ToArray());
                }
            }

            var boundNames = functions.Select(static function => function.Symbol.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (boundNames.SetEquals(functionsByName.Keys))
            {
                diagnostics.AddRange(failedDiagnostics.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).SelectMany(static pair => pair.Value));
                diagnostics.AddRange(roundDiagnostics);
                break;
            }
            functionsByName = functionsByName
                .Where(pair => boundNames.Contains(pair.Key))
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        var boundDocuments = orderedDocuments.Select(document => new PowerShellBoundSourceDocument(
            document.DocumentId,
            document.Path,
            PowerShellSourceParser.GetSpan(document, document.SyntaxRoot.Extent),
            declarations.Where(declaration => declaration.Document.DocumentId == document.DocumentId)
                .Select(static declaration => declaration.Symbol)
                .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
                .ToArray())).ToArray();

        return new PowerShellBoundProgram(
            boundDocuments,
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            OrderDiagnostics(diagnostics));
    }

    private static bool HasTypedFunctionShape(ScriptBlockAst body, PowerShellCompilationCapability capabilities)
        => (body.DynamicParamBlock is null &&
            body.BeginBlock is null &&
            body.ProcessBlock is null &&
            GetCleanBlock(body) is null) ||
           PowerShellRuntimeFreePipelineLifecyclePolicy.TryGetPipelineParameter(body, capabilities, out _, out _);

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst body)
        => body.GetType().GetProperty("CleanBlock")?.GetValue(body) as NamedBlockAst;
}
