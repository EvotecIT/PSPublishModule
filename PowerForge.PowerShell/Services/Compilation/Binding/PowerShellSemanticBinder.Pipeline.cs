using System.Globalization;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private bool TryBindRuntimeFreePipelineEnumeration(
        ParsedSourceDocument document,
        PipelineAst pipeline,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        out PowerShellBoundForEachStatement? result)
    {
        result = null;
        if (!PowerShellMappingCommandSemanticBinder.TryGetRuntimeFreeProcess(
                pipeline,
                _commandRegistry,
                out var inputSyntax,
                out var processBlock))
            return false;

        var contextualInputType = InferPipelineInputType(inputSyntax, symbols);
        var input = BindExpression(
            document,
            inputSyntax,
            symbols,
            functions,
            diagnostics,
            contextualInputType,
            targetFramework: targetFramework,
            capabilities: capabilities);
        if (input is null)
            return true;

        var collectionType = input.Type.ClrType;
        if (input is not PowerShellBoundArrayExpression ||
            !collectionType.IsArray ||
            collectionType.GetArrayRank() != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2901",
                "Runtime-free pipeline enumeration requires a statically allocated, one-dimensional typed array expression; nullable array variables remain hosted.",
                input.Span));
            return true;
        }

        var elementType = collectionType.GetElementType()!;
        if (!PowerShellStableScalarTypePolicy.IsSupported(elementType))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2903",
                $"Runtime-free pipeline enumeration requires a stable scalar element type; '{elementType.FullName}' may have nested or provider-defined enumeration semantics.",
                input.Span));
            return true;
        }
        var itemName = CreatePipelineItemName(symbols, pipeline.Extent.StartOffset);
        var itemSpan = PowerShellSourceParser.GetSpan(document, processBlock.Extent);
        var itemSymbol = new PowerShellSymbolId(
            PowerShellSymbolKind.PipelineVariable,
            document.DocumentId,
            itemName,
            itemSpan,
            "pipeline/" + pipeline.Extent.StartOffset.ToString(CultureInfo.InvariantCulture) + "/item");
        var itemType = new PowerShellTypeFact(
            elementType,
            PowerShellTypeFactProvenance.Inferred,
            "The bounded pipeline input provides one stable CLR current-item type.");
        var processSymbols = symbols.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        processSymbols["_"] = new PowerShellSemanticSymbolBinding(itemSymbol, itemType);
        processSymbols["PSItem"] = new PowerShellSemanticSymbolBinding(itemSymbol, itemType);
        var body = BindPipelineProcessBlock(document, processBlock, processSymbols, functions, diagnostics, targetFramework, capabilities);
        if (body is null)
            return true;
        if (body.Statements.Length == 0 || body.Statements.Any(statement =>
                statement is not PowerShellBoundAssignmentStatement assignment ||
                assignment.Target.StableKey == itemSymbol.StableKey))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2902",
                "Runtime-free pipeline enumeration through ForEach-Object currently accepts only non-output assignments to enclosing typed locals; process output and control flow remain hosted.",
                itemSpan));
            return true;
        }

        result = new PowerShellBoundForEachStatement(
            PowerShellSourceParser.GetSpan(document, pipeline.Extent),
            itemSymbol,
            elementType,
            input,
            scalarString: false,
            body,
            declareVariable: true);
        return true;
    }

    private static Type? InferPipelineInputType(
        ExpressionAst input,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols)
    {
        if (input is not ArrayLiteralAst literal || literal.Elements.Count == 0)
            return null;
        var elementTypes = literal.Elements.Select(element => element switch
            {
                StringConstantExpressionAst => typeof(string),
                ConstantExpressionAst constant => constant.Value?.GetType() ?? typeof(object),
                VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var binding) => binding.Type.ClrType,
                ConvertExpressionAst conversion when conversion.StaticType != typeof(object) => conversion.StaticType,
                _ => element.StaticType != typeof(object) ? element.StaticType : null
            })
            .Distinct()
            .ToArray();
        return elementTypes.Length == 1 && elementTypes[0] is not null
            ? elementTypes[0]!.MakeArrayType()
            : null;
    }

    private PowerShellBoundBlock? BindPipelineProcessBlock(
        ParsedSourceDocument document,
        NamedBlockAst processBlock,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in processBlock.Statements)
        {
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, functions, diagnostics, isTerminal: false, targetFramework, capabilities);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2902",
                        $"ForEach-Object pipeline enumeration process statement '{statement.GetType().Name}' is outside the bounded runtime-free assignment contract.",
                        PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            statements.Add(bound);
        }
        return new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, processBlock.Extent), statements.ToArray());
    }

    private static string CreatePipelineItemName(
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        int offset)
    {
        var suffix = offset.ToString(CultureInfo.InvariantCulture);
        var candidate = "__pf_pipeline_item_" + suffix;
        var used = symbols.Values
            .Select(static binding => PowerShellCSharpSymbolRenderer.Identifier(binding.Symbol.Name))
            .ToHashSet(StringComparer.Ordinal);
        var sequence = 0;
        while (used.Contains(PowerShellCSharpSymbolRenderer.Identifier(candidate)))
            candidate = "__pf_pipeline_item_" + suffix + "_" + (++sequence).ToString(CultureInfo.InvariantCulture);
        return candidate;
    }
}
