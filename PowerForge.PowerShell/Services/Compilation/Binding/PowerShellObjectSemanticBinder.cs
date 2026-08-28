using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellObjectSemanticBinder
{
    internal static PowerShellTypeFact InferLiteralType(ConvertExpressionAst conversion)
    {
        var properties = new Dictionary<string, PowerShellTypeFact>(StringComparer.OrdinalIgnoreCase);
        if (conversion.Child is HashtableAst hashtable)
        {
            foreach (var pair in hashtable.KeyValuePairs)
            {
                if (pair.Item1 is not StringConstantExpressionAst key ||
                    pair.Item2 is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                    pipeline.PipelineElements[0] is not CommandExpressionAst command)
                    continue;
                var type = command.Expression.StaticType == typeof(object)
                    ? PowerShellTypeFact.Unknown
                    : new PowerShellTypeFact(command.Expression.StaticType, PowerShellTypeFactProvenance.Inferred, "The literal note-property expression provides a bounded property type.");
                properties[key.Value] = type;
            }
        }
        return new PowerShellTypeFact(
            typeof(System.Management.Automation.PSObject),
            PowerShellTypeFactProvenance.Inferred,
            "A [pscustomobject] literal provides one statically known note-property shape.",
            properties);
    }

    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        ConvertExpressionAst conversion,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, conversion.Extent);
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) || conversion.Child is not HashtableAst hashtable)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2901", "Typed [pscustomobject] construction requires PowerShell runtime support through a generated binary-module host.", span));
            return null;
        }
        var properties = new List<PowerShellBoundNoteProperty>();
        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (pair.Item1 is not StringConstantExpressionAst key || string.IsNullOrWhiteSpace(key.Value))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2902", "Typed [pscustomobject] literals require non-empty literal string property names.", PowerShellSourceParser.GetSpan(document, pair.Item1.Extent)));
                return null;
            }
            if (pair.Item2 is not PipelineAst { PipelineElements.Count: 1 } pipeline || pipeline.PipelineElements[0] is not CommandExpressionAst command)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2903", "Typed [pscustomobject] note-property values must be one scalar expression.", PowerShellSourceParser.GetSpan(document, pair.Item2.Extent)));
                return null;
            }
            var value = bindExpression(command.Expression, null);
            if (value is null || value.Type.ClrType == typeof(void)) return null;
            properties.Add(new PowerShellBoundNoteProperty(key.Value, value));
        }
        return new PowerShellBoundPowerShellObjectExpression(span, properties.ToArray());
    }

    internal static bool TryBindKnownPropertiesValue(
        ParsedSourceDocument document,
        MemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        out PowerShellBoundExpression? bound)
    {
        bound = null;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) ||
            syntax.Member is not StringConstantExpressionAst { Value: var terminal } ||
            !terminal.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
            syntax.Expression is not IndexExpressionAst
            {
                Target: MemberExpressionAst
                {
                    Expression: MemberExpressionAst
                    {
                        Expression: var receiverSyntax,
                        Member: StringConstantExpressionAst { Value: var psObjectMember }
                    },
                    Member: StringConstantExpressionAst { Value: var propertiesMember }
                },
                Index: StringConstantExpressionAst { Value: var propertyName }
            } ||
            !psObjectMember.Equals("PSObject", StringComparison.OrdinalIgnoreCase) ||
            !propertiesMember.Equals("Properties", StringComparison.OrdinalIgnoreCase))
            return false;

        var receiver = bindExpression(receiverSyntax, null);
        if (receiver is null || !receiver.Type.TryGetKnownProperty(propertyName, out var propertyType))
            return false;
        bound = new PowerShellBoundClrMemberExpression(
            PowerShellSourceParser.GetSpan(document, syntax.Extent),
            receiver.Type.ClrType,
            propertyName,
            isStatic: false,
            receiver,
            PowerShellClrReceiverBehavior.PowerShellAdapter,
            propertyType);
        return true;
    }

    internal static bool TryBindAddMember(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        PowerShellCompilationCapability capabilities,
        PowerShellCommandSemanticRegistry commandRegistry,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        out PowerShellBoundStatement? bound)
    {
        bound = null;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects) ||
            statement is not PipelineAst { PipelineElements.Count: 2 } pipeline ||
            pipeline.PipelineElements[0] is not CommandExpressionAst { Expression: VariableExpressionAst receiverSyntax } ||
            pipeline.PipelineElements[1] is not CommandAst command)
            return false;

        var resolution = commandRegistry.Resolve(command.GetCommandName());
        if (resolution.Status != PowerShellCommandResolutionStatus.Resolved ||
            resolution.Contract!.Family != PowerShellCompilationCommandFamily.ObjectMutation)
            return false;

        if (!symbols.TryGetValue(receiverSyntax.VariablePath.UserPath, out var receiverBinding) ||
            receiverBinding.Type.ClrType != typeof(System.Management.Automation.PSObject))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2910", "Bounded Add-Member requires a local [pscustomobject] value with a statically known shape.", PowerShellSourceParser.GetSpan(document, pipeline.Extent)));
            return true;
        }

        var provider = resolution.Contract;
        if (!TryGetExactNamedArguments(command, provider.Parameters, out var arguments) ||
            !arguments.TryGetValue("NotePropertyName", out var nameSyntax) ||
            nameSyntax is not StringConstantExpressionAst { Value: var propertyName } ||
            string.IsNullOrWhiteSpace(propertyName) ||
            !arguments.TryGetValue("NotePropertyValue", out var valueSyntax))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2911", "Bounded Add-Member supports literal -NotePropertyName and one -NotePropertyValue expression without PassThru or force semantics.", PowerShellSourceParser.GetSpan(document, command.Extent)));
            return true;
        }

        var receiver = bindExpression(receiverSyntax, null);
        var value = bindExpression(valueSyntax, null);
        if (receiver is null || value is null) return true;
        if (receiverBinding.Type.TryGetKnownProperty(propertyName, out _))
        {
            var errorProvider = commandRegistry.Resolve("Write-Error");
            if (errorProvider.Status != PowerShellCommandResolutionStatus.Resolved)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2912",
                    "Bounded duplicate Add-Member requires the canonical Write-Error stream provider.",
                    PowerShellSourceParser.GetSpan(document, command.Extent)));
                return true;
            }
            var message = $"Cannot add a member with the name '{propertyName}' because a member with that name already exists. To overwrite the member anyway, add the Force parameter to your command.";
            bound = new PowerShellBoundStreamWriteStatement(
                PowerShellSourceParser.GetSpan(document, pipeline.Extent),
                PowerShellStreamCommandKind.Error,
                errorProvider.Contract!,
                new PowerShellBoundLiteralExpression(
                    PowerShellSourceParser.GetSpan(document, command.Extent),
                    message,
                    new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal, "The statically known duplicate Add-Member contract owns its non-terminating error text."),
                    PowerShellValueState.Known));
            return true;
        }
        receiverBinding.AddKnownProperty(propertyName, value.Type);
        bound = new PowerShellBoundClrMemberAssignmentStatement(
            PowerShellSourceParser.GetSpan(document, pipeline.Extent),
            receiver,
            receiver.Type.ClrType,
            propertyName,
            PowerShellClrReceiverBehavior.PowerShellAdapterAddNoteProperty,
            value);
        return true;
    }

    private static bool TryGetExactNamedArguments(
        CommandAst command,
        IReadOnlyList<PowerShellCompilationCommandParameterContract> contracts,
        out IReadOnlyDictionary<string, CommandElementAst> arguments)
    {
        var elements = command.CommandElements;
        var values = new Dictionary<string, CommandElementAst>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < elements.Count; index++)
        {
            if (elements[index] is not CommandParameterAst parameter)
                return Fail(out arguments);
            var contract = contracts.SingleOrDefault(candidate =>
                parameter.ParameterName.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase) ||
                candidate.Aliases.Any(alias => parameter.ParameterName.Equals(alias, StringComparison.OrdinalIgnoreCase)));
            if (contract is null || values.ContainsKey(contract.Name))
                return Fail(out arguments);
            CommandElementAst? argument = parameter.Argument;
            if (argument is null && ++index < elements.Count && elements[index] is not CommandParameterAst)
                argument = elements[index];
            if (argument is null)
                return Fail(out arguments);
            values.Add(contract.Name, argument);
        }
        arguments = values;
        return values.Count == contracts.Count;

        static bool Fail(out IReadOnlyDictionary<string, CommandElementAst> result)
        {
            result = null!;
            return false;
        }
    }
}
