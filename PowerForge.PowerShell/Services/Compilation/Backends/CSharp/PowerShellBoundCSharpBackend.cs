using System.Globalization;
using System.Text;

namespace PowerForge;

/// <summary>
/// Renders already-lowered CLR operations as deterministic readable C#.
/// </summary>
internal sealed partial class PowerShellBoundCSharpBackend
{
    internal PowerShellBoundCSharpResult Emit(PowerShellLoweredProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        var methods = program.Functions.Select(function => EmitFunction(function, program.TargetCapabilities)).ToArray();
        return new PowerShellBoundCSharpResult(methods, program.Diagnostics.ToArray());
    }

    private static PowerShellCSharpMethodEmission EmitFunction(
        PowerShellLoweredFunction function,
        PowerShellCompilationCapability targetCapabilities)
    {
        var builder = new StringBuilder();
        var sourceMap = new List<PowerShellCompilationSourceMapEntry>();
        var parameterParts = function.Parameters.Select(parameter =>
            $"{PowerShellCSharpSymbolRenderer.TypeName(parameter.ClrType)} {PowerShellCSharpSymbolRenderer.Identifier(parameter.Symbol.Name)}").ToList();
        var requiresBoundParameters = function.RequiresPowerShellBoundParameters || function.Parameters.Any(parameter =>
            parameter.Contract.DefaultValue is not null ||
            !parameter.Contract.IsMandatory && parameter.Contract.Validations.Length > 0 &&
            targetCapabilities.HasFlag(PowerShellCompilationCapability.BoundParameters));
        AddHostParameters(parameterParts, function, requiresBoundParameters);
        var parameters = string.Join(", ", parameterParts);
        var usedIdentifiers = function.Parameters.Select(static parameter => PowerShellClrSymbolMapper.MapIdentifier(parameter.Symbol.Name))
            .Concat(function.Locals.Select(static local => PowerShellClrSymbolMapper.MapIdentifier(local.Symbol.Name)))
            .ToHashSet(StringComparer.Ordinal);
        var temporaryIndex = 0;
        string GetTemporaryIdentifier(string prefix)
        {
            string candidate;
            do { candidate = $"__{prefix}_{temporaryIndex++}"; } while (!usedIdentifiers.Add(candidate));
            return candidate;
        }
        var discardHelper = ContainsDiscardValue(function.Statements)
            ? GetTemporaryIdentifier("discardValue")
            : null;
        builder.Append("    public static ")
            .Append(PowerShellCSharpSymbolRenderer.TypeName(function.ReturnType))
            .Append(' ')
            .Append(function.GeneratedName)
            .Append('(')
            .Append(parameters)
            .AppendLine(")")
            .AppendLine("    {")
            .AppendLine("        checked")
            .AppendLine("        {");

        if (targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            builder.AppendLine("            static T __powerForgeConvertInvariant<T>(object? value)")
                .AppendLine("            {")
                .AppendLine("                if (global::System.Management.Automation.LanguagePrimitives.TryConvertTo<T>(value, global::System.Globalization.CultureInfo.InvariantCulture, out T result)) return result;")
                .AppendLine("                return (T)global::System.Management.Automation.LanguagePrimitives.ConvertTo(value, typeof(T), global::System.Globalization.CultureInfo.InvariantCulture)!;")
                .AppendLine("            }");
        }
        if (discardHelper is not null)
        {
            builder.Append("            static void ").Append(discardHelper).AppendLine("<T>(T value) { }");
        }
        var parameterContracts = function.Parameters.Select(static parameter => new PowerShellParameterEmissionContract(
            parameter.Symbol.Name,
            parameter.ClrType,
            parameter.Contract)).ToArray();
        var prologue = new PowerShellParameterPrologueRenderer(
            targetCapabilities,
            PowerShellCSharpSymbolRenderer.TypeName,
            GetTemporaryIdentifier).Render(parameterContracts);
        if (prologue.Length > 0)
        {
            foreach (var line in prologue.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                builder.Append("            ").AppendLine(line);
        }

        foreach (var statement in function.Statements)
            EmitStatement(builder, statement, 3, GetTemporaryIdentifier, discardHelper, sourceMap);

        builder.AppendLine("        }").Append("    }");
        var commandProviders = PowerShellLoweredCommandProviderCollector.Collect(function.Statements);
        var hostedRegionSiteCount = CountHostedRegionSites(function.Statements);
        var supportsBasicCommandDiscoverySurface =
            function.RequiresPowerShellCommandRegions &&
            !function.RequiresPowerShellStreams &&
            !ContainsNonDiscoveryHostedBoundary(function.Statements) &&
            commandProviders.Length > 0 &&
            commandProviders.All(static provider => provider.Family == PowerShellCompilationCommandFamily.CommandDiscovery);
        return new PowerShellCSharpMethodEmission(
            function.GeneratedName,
            function.ReturnType,
            builder.ToString(),
            function.Span,
            requiresPowerShellStreams: function.RequiresPowerShellStreams,
            requiresProviderCancellation: function.RequiresProviderCancellation,
            requiresPowerShellCommandRegions: function.RequiresPowerShellCommandRegions,
            requiresPowerShellBoundParameters: requiresBoundParameters,
            requiresPowerShellRuntimeState: function.RequiresPowerShellRuntimeState,
            help: function.Help?.ToPublicModel(),
            declaredOutputType: function.DeclaredOutputType,
            declaredOutputTypeName: function.DeclaredOutputTypeName,
            aliases: function.Aliases.ToArray(),
            commandBinding: function.CommandBinding,
            sourceMap: sourceMap.ToArray(),
            commandProviders: commandProviders,
            outputCardinality: function.OutputCardinality.ToString(),
            outputValueStates: function.OutputValueStates.Select(static state => state.ToString()).ToArray(),
            collectionElementType: function.OutputCardinality == PowerShellOutputCardinality.Collection
                ? function.CollectionElementType?.FullName ?? typeof(object).FullName!
                : string.Empty,
            outputScalarization: function.OutputCardinality switch
            {
                PowerShellOutputCardinality.None => "NoOutput",
                PowerShellOutputCardinality.Collection => "EnumerateCollection",
                PowerShellOutputCardinality.Scalar => "PreserveScalar",
                _ => "RuntimeDependent"
            },
            hostedRegionSiteCount: hostedRegionSiteCount,
            supportsBasicCommandDiscoverySurface: supportsBasicCommandDiscoverySurface);
    }

    private static void EmitStatement(
        StringBuilder builder,
        PowerShellLoweredStatement statement,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        builder.Append(prefix).Append("#line ")
            .Append(statement.Span.StartLine.ToString(CultureInfo.InvariantCulture))
            .Append(" \"").Append(statement.Span.DocumentId).AppendLine("\"");
        var start = PowerShellGeneratedSourcePosition.Get(builder);
        EmitStatementCore(builder, statement, indent, getTemporaryIdentifier, discardHelper, sourceMap);
        var end = PowerShellGeneratedSourcePosition.Get(builder);
        builder.Append(prefix).AppendLine("#line default");
        sourceMap.Add(new PowerShellCompilationSourceMapEntry(
            statement.Span.StartLine,
            statement.Span.StartColumn,
            statement.Span.EndLine,
            statement.Span.EndColumn,
            start.Line,
            start.Column,
            end.Line,
            end.Column));
    }

    private static void EmitStatementCore(
        StringBuilder builder,
        PowerShellLoweredStatement statement,
        int indent,
        Func<string, string> getTemporaryIdentifier,
        string? discardHelper,
        ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var prefix = new string(' ', indent * 4);
        switch (statement)
        {
            case PowerShellLoweredLocalDeclarationStatement declaration:
                builder.Append(prefix).Append(PowerShellCSharpSymbolRenderer.TypeName(declaration.ClrType)).Append(' ')
                    .Append(PowerShellCSharpSymbolRenderer.Identifier(declaration.Symbol.Name)).AppendLine(" = default!;");
                return;
            case PowerShellLoweredAssignmentStatement assignment:
                builder.Append(prefix);
                if (assignment.Declare) builder.Append(PowerShellCSharpSymbolRenderer.TypeName(assignment.ClrType)).Append(' ');
                builder.Append(EmitMutation(
                    assignment.Target,
                    assignment.ClrType,
                    assignment.Operation,
                    assignment.Value,
                    assignment.NormalizeNullString,
                    assignment.CheckedIntegral)).AppendLine(";");
                return;
            case PowerShellLoweredIndexAssignmentStatement assignment:
                builder.Append(prefix).Append(EmitIndexAssignment(assignment)).AppendLine(";");
                return;
            case PowerShellLoweredClrMemberAssignmentStatement assignment:
                builder.Append(prefix).Append(EmitClrMemberAssignment(assignment)).AppendLine(";");
                return;
            case PowerShellLoweredReturnStatement { Expression: null }:
                builder.Append(prefix).AppendLine("return;");
                return;
            case PowerShellLoweredReturnStatement { Expression.ClrType: var type } returned when type == typeof(void):
                builder.Append(prefix).Append(EmitExpression(returned.Expression!)).AppendLine(";");
                builder.Append(prefix).AppendLine("return;");
                return;
            case PowerShellLoweredReturnStatement { EmitsValue: false } returned:
                builder.Append(prefix).Append(EmitExpression(returned.Expression!)).AppendLine(";");
                builder.Append(prefix).AppendLine("return;");
                return;
            case PowerShellLoweredReturnStatement returned:
                builder.Append(prefix).Append("return ").Append(EmitExpression(returned.Expression!)).AppendLine(";");
                return;
            case PowerShellLoweredExpressionStatement { DiscardValue: true } expression:
                builder.Append(prefix).Append(discardHelper).Append('<')
                    .Append(PowerShellCSharpSymbolRenderer.TypeName(expression.Expression.ClrType)).Append(">(")
                    .Append(EmitExpression(expression.Expression)).AppendLine(");");
                return;
            case PowerShellLoweredExpressionStatement expression:
                builder.Append(prefix).Append(EmitExpression(expression.Expression)).AppendLine(";");
                return;
            case PowerShellLoweredStreamWriteStatement stream:
                EmitProviderStreamWrite(builder, stream, prefix, getTemporaryIdentifier);
                return;
            case PowerShellLoweredCommandRegionStatement region:
                builder.Append(prefix).Append("__invokePowerShellRegion(")
                    .Append(PowerShellCSharpLiteral.QuoteString(region.HostedFallbackSource))
                    .Append(", ").Append(EmitCommandRegionArguments(region.Arguments)).AppendLine(");");
                return;
            case PowerShellLoweredCommandCaptureStatement capture:
                EmitCommandCapture(builder, capture, prefix);
                return;
            case PowerShellLoweredIfStatement conditional:
                for (var index = 0; index < conditional.Clauses.Length; index++)
                {
                    var clause = conditional.Clauses[index];
                    builder.Append(prefix).Append(index == 0 ? "if (" : "else if (").Append(EmitExpression(clause.Condition)).AppendLine(")");
                    EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                }
                if (conditional.ElseStatements is not null)
                {
                    builder.Append(prefix).AppendLine("else");
                    EmitBlock(builder, conditional.ElseStatements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                }
                return;
            case PowerShellLoweredWhileStatement loop:
                if (loop.Kind == PowerShellLoweredLoopKind.While)
                {
                    builder.Append(prefix).Append("while (").Append(EmitExpression(loop.Condition)).AppendLine(")");
                    EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                }
                else
                {
                    builder.Append(prefix).AppendLine("do");
                    EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                    builder.Append(prefix).Append("while (");
                    if (loop.Kind == PowerShellLoweredLoopKind.DoUntil) builder.Append("!(");
                    builder.Append(EmitExpression(loop.Condition));
                    if (loop.Kind == PowerShellLoweredLoopKind.DoUntil) builder.Append(')');
                    builder.AppendLine(");");
                }
                return;
            case PowerShellLoweredForStatement loop:
                var initializer = loop.Initializer is null
                    ? string.Empty
                    : (loop.DeclareInitializer ? PowerShellCSharpSymbolRenderer.TypeName(loop.Initializer.TargetClrType) + " " : string.Empty) + EmitExpression(loop.Initializer);
                var condition = loop.Condition is null ? "true" : EmitExpression(loop.Condition);
                var iterator = loop.Iterator is null ? string.Empty : EmitExpression(loop.Iterator);
                builder.Append(prefix).Append("for (").Append(initializer).Append("; ").Append(condition).Append("; ").Append(iterator).AppendLine(")");
                EmitBlock(builder, loop.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                return;
            case PowerShellLoweredForEachStatement loop:
                EmitForEach(builder, loop, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                return;
            case PowerShellLoweredSwitchStatement switchStatement:
                EmitSwitch(builder, switchStatement, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                return;
            case PowerShellLoweredThrowStatement { Expression: null }:
                builder.Append(prefix).AppendLine("throw;");
                return;
            case PowerShellLoweredThrowStatement thrown:
                builder.Append(prefix).Append("throw ").Append(EmitExpression(thrown.Expression!)).AppendLine(";");
                return;
            case PowerShellLoweredTryStatement tryStatement:
                builder.Append(prefix).AppendLine("try");
                EmitBlock(builder, tryStatement.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                foreach (var clause in tryStatement.Catches)
                {
                    if (clause.ExceptionTypes.Length == 0)
                    {
                        builder.Append(prefix).AppendLine("catch (global::System.Exception)");
                        EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                        continue;
                    }
                    foreach (var exceptionType in clause.ExceptionTypes)
                    {
                        builder.Append(prefix).Append("catch (").Append(PowerShellCSharpSymbolRenderer.TypeName(exceptionType)).AppendLine(")");
                        EmitBlock(builder, clause.Statements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                    }
                }
                if (tryStatement.FinallyStatements is not null)
                {
                    builder.Append(prefix).AppendLine("finally");
                    EmitBlock(builder, tryStatement.FinallyStatements, indent, getTemporaryIdentifier, discardHelper, sourceMap);
                }
                return;
            case PowerShellLoweredBreakStatement:
                builder.Append(prefix).AppendLine("break;");
                return;
            case PowerShellLoweredContinueStatement:
                builder.Append(prefix).AppendLine("continue;");
                return;
            default:
                throw new InvalidOperationException($"Lowered statement '{statement.GetType().Name}' has no C# rendering owner.");
        }
    }

    private static string EmitExpression(PowerShellLoweredExpression expression)
        => expression switch
        {
            PowerShellLoweredLiteralExpression literal => EmitLiteral(literal),
            PowerShellLoweredVariableExpression variable => PowerShellCSharpSymbolRenderer.Identifier(variable.Symbol.Name),
            PowerShellLoweredRuntimeStateExpression runtime => EmitRuntimeState(runtime),
            PowerShellLoweredCommandAvailabilityExpression discovery => EmitCommandAvailability(discovery),
            PowerShellLoweredParameterPresenceExpression presence => $"__boundParameters.Contains({PowerShellCSharpLiteral.QuoteString(presence.ParameterName)})",
            PowerShellLoweredConversionExpression conversion => EmitConversion(conversion),
            PowerShellLoweredBinaryExpression binary => EmitBinary(binary),
            PowerShellLoweredUnaryExpression unary => EmitUnary(unary),
            PowerShellLoweredTypeTestExpression typeTest => EmitTypeTest(typeTest),
            PowerShellLoweredRegexExpression regex => EmitRegex(regex),
            PowerShellLoweredWildcardExpression wildcard => EmitWildcard(wildcard),
            PowerShellLoweredMembershipExpression membership => EmitMembership(membership),
            PowerShellLoweredStringSplitExpression split => EmitStringSplit(split),
            PowerShellLoweredStringJoinExpression join => EmitStringJoin(join),
            PowerShellLoweredInterpolatedStringExpression interpolated => EmitInterpolatedString(interpolated),
            PowerShellLoweredMutationExpression mutation => EmitMutation(
                mutation.Target,
                mutation.TargetClrType,
                mutation.Operation,
                mutation.Value,
                mutation.NormalizeNullString,
                mutation.CheckedIntegral),
            PowerShellLoweredArrayExpression array => EmitArray(array),
            PowerShellLoweredArrayConcatenationExpression concatenation => EmitArrayConcatenation(concatenation),
            PowerShellLoweredDictionaryExpression dictionary => EmitDictionary(dictionary),
            PowerShellLoweredPowerShellObjectExpression powerShellObject => EmitPowerShellObject(powerShellObject),
            PowerShellLoweredIndexExpression index => EmitIndex(index),
            PowerShellLoweredClrMemberExpression member => EmitClrMember(member),
            PowerShellLoweredClrInvocationExpression invocation => EmitClrInvocation(invocation),
            PowerShellLoweredInvocationExpression invocation => EmitLocalInvocation(invocation),
            _ => throw new InvalidOperationException($"Lowered expression '{expression.GetType().Name}' has no C# rendering owner.")
        };

    private static string EmitCommandAvailability(PowerShellLoweredCommandAvailabilityExpression discovery)
    {
        const string script = "param([string] $__pfName, [string] $__pfErrorAction) [bool](Microsoft.PowerShell.Core\\Get-Command -Name $__pfName -ErrorAction $__pfErrorAction)";
        var errorAction = discovery.ErrorAction switch
        {
            PowerShellCommandDiscoveryErrorAction.Ignore => "Ignore",
            PowerShellCommandDiscoveryErrorAction.SilentlyContinue => "SilentlyContinue",
            _ => throw new InvalidOperationException($"Unsupported command-discovery error action '{discovery.ErrorAction}'.")
        };
        return "global::System.Management.Automation.LanguagePrimitives.IsTrue(__invokePowerShellCapture(" +
               PowerShellCSharpLiteral.QuoteString(script) + ", new object?[] { " +
               EmitExpression(discovery.Name) + ", " + PowerShellCSharpLiteral.QuoteString(errorAction) + " }))";
    }

    private static string EmitRuntimeState(PowerShellLoweredRuntimeStateExpression expression)
    {
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ShouldProcessTarget)
            return $"__shouldProcessTarget({EmitExpression(expression.Arguments[0])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ShouldProcessAction)
            return $"__shouldProcessAction({EmitExpression(expression.Arguments[0])}, {EmitExpression(expression.Arguments[1])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.EnvironmentVariable)
            return $"global::System.Environment.GetEnvironmentVariable({EmitExpression(expression.Arguments[0])})";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ActionPreference)
            return $"(global::System.Management.Automation.ActionPreference)global::System.Management.Automation.LanguagePrimitives.ConvertTo(__runtimeState[{EmitExpression(expression.Arguments[0])}], typeof(global::System.Management.Automation.ActionPreference), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ConfirmPreference)
            return $"(global::System.Management.Automation.ConfirmImpact)global::System.Management.Automation.LanguagePrimitives.ConvertTo(__runtimeState[{EmitExpression(expression.Arguments[0])}], typeof(global::System.Management.Automation.ConfirmImpact), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.ErrorCollection)
            return $"(global::System.Collections.ArrayList)__runtimeState[{EmitExpression(expression.Arguments[0])}]";
        if (expression.Kind == PowerShellRuntimeStateIntrinsicKind.LanguageMode)
            return $"(global::System.Management.Automation.PSLanguageMode)__runtimeState[{EmitExpression(expression.Arguments[0])}]!";
        return PowerShellRuntimeStateIntrinsicPolicy.EmitStatic(expression.Kind, expression.TargetFramework, expression.SemanticProfileId);
    }

    private static string EmitConversion(PowerShellLoweredConversionExpression conversion)
    {
        var type = PowerShellCSharpSymbolRenderer.TypeName(conversion.ClrType);
        if (conversion.UsePowerShellTruthiness)
            return $"global::System.Management.Automation.LanguagePrimitives.IsTrue((object?)({EmitExpression(conversion.Operand)}))";
        return conversion.UsePowerShellLanguageRuntime
            ? $"__powerForgeConvertInvariant<{type}>((object?)({EmitExpression(conversion.Operand)}))"
            : $"({type})({EmitExpression(conversion.Operand)})";
    }

    private static string EmitTypeTest(PowerShellLoweredTypeTestExpression expression)
    {
        var operand = EmitExpression(expression.Operand);
        if (Nullable.GetUnderlyingType(expression.TargetType) is not null)
            return $"new global::System.Func<bool>(() => {{ _ = (object?)({operand}); return {(expression.Negate ? "true" : "false")}; }})()";
        var test = $"((object?)({operand}) is {PowerShellCSharpSymbolRenderer.TypeName(expression.TargetType)})";
        return expression.Negate ? $"!{test}" : test;
    }

    private static string EmitRegex(PowerShellLoweredRegexExpression expression)
    {
        var input = EmitExpression(expression.Input);
        var pattern = EmitExpression(expression.Pattern);
        var options = expression.IgnoreCase
            ? "global::System.Text.RegularExpressions.RegexOptions.IgnoreCase"
            : "global::System.Text.RegularExpressions.RegexOptions.None";
        if (expression.Operation == PowerShellBoundRegexOperation.Replace)
        {
            var replacement = EmitExpression(expression.Replacement!);
            return $"global::System.Text.RegularExpressions.Regex.Replace(({input} ?? string.Empty), ({pattern} ?? string.Empty), ({replacement} ?? string.Empty), {options})";
        }
        var match = $"global::System.Text.RegularExpressions.Regex.IsMatch(({input} ?? string.Empty), ({pattern} ?? string.Empty), {options})";
        return expression.Operation == PowerShellBoundRegexOperation.NotMatch ? $"!({match})" : match;
    }

    private static string EmitWildcard(PowerShellLoweredWildcardExpression expression)
    {
        var options = expression.IgnoreCase
            ? "global::System.Management.Automation.WildcardOptions.IgnoreCase"
            : "global::System.Management.Automation.WildcardOptions.None";
        var match = $"new global::System.Management.Automation.WildcardPattern(({expression.PatternTemporary} ?? string.Empty), {options}).IsMatch(({expression.InputTemporary} ?? string.Empty))";
        if (expression.Negate) match = $"!({match})";
        return $"new global::System.Func<bool>(() => {{ var {expression.InputTemporary} = {EmitExpression(expression.Input)}; var {expression.PatternTemporary} = {EmitExpression(expression.Pattern)}; return {match}; }})()";
    }

    private static string EmitMembership(PowerShellLoweredMembershipExpression expression)
    {
        var collection = expression.CollectionOnRight ? expression.RightTemporary : expression.LeftTemporary;
        var candidate = expression.CollectionOnRight ? expression.LeftTemporary : expression.RightTemporary;
        var comparison = $"global::System.Linq.Enumerable.Any(({collection} ?? global::System.Array.Empty<{PowerShellCSharpSymbolRenderer.TypeName(expression.ElementType)}>()), {expression.ItemTemporary} => global::System.Management.Automation.LanguagePrimitives.Equals((object?){expression.ItemTemporary}, (object?)({candidate}), {(expression.IgnoreCase ? "true" : "false")}, global::System.Globalization.CultureInfo.InvariantCulture))";
        if (expression.Negate) comparison = $"!({comparison})";
        return $"new global::System.Func<bool>(() => {{ var {expression.LeftTemporary} = {EmitExpression(expression.Left)}; var {expression.RightTemporary} = {EmitExpression(expression.Right)}; return {comparison}; }})()";
    }

    private static string EmitDictionary(PowerShellLoweredDictionaryExpression dictionary)
    {
        var entries = string.Join(", ", dictionary.Entries.Select(entry => $"{{ {EmitExpression(entry.Key)}, {EmitExpression(entry.Value)} }}"));
        return dictionary.Kind switch
        {
            PowerShellBoundDictionaryKind.OrderedStringDictionary or PowerShellBoundDictionaryKind.OrderedObjectDictionary =>
                $"new global::System.Collections.Specialized.OrderedDictionary(global::System.StringComparer.OrdinalIgnoreCase) {{ {entries} }}",
            PowerShellBoundDictionaryKind.ObjectDictionary =>
                $"new global::System.Collections.Hashtable(global::System.StringComparer.OrdinalIgnoreCase) {{ {entries} }}",
            _ => $"new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.OrdinalIgnoreCase) {{ {entries} }}"
        };
    }

    private static string EmitPowerShellObject(PowerShellLoweredPowerShellObjectExpression powerShellObject)
    {
        var statements = new List<string>
        {
            $"var {powerShellObject.Temporary} = new global::System.Management.Automation.PSObject();"
        };
        statements.AddRange(powerShellObject.Properties.Select(property =>
            $"{powerShellObject.Temporary}.Properties.Add(new global::System.Management.Automation.PSNoteProperty({PowerShellCSharpLiteral.QuoteString(property.Name)}, {EmitExpression(property.Value)}));"));
        statements.Add($"return {powerShellObject.Temporary};");
        return "new global::System.Func<global::System.Management.Automation.PSObject>(() => { " + string.Join(" ", statements) + " })()";
    }

    private static string EmitStringSplit(PowerShellLoweredStringSplitExpression split)
    {
        var options = split.IgnoreCase
            ? "global::System.Text.RegularExpressions.RegexOptions.IgnoreCase"
            : "global::System.Text.RegularExpressions.RegexOptions.None";
        return $"global::System.Text.RegularExpressions.Regex.Split(({EmitExpression(split.Input)} ?? string.Empty), ({EmitExpression(split.Pattern)} ?? string.Empty), {options})";
    }

    private static string EmitStringJoin(PowerShellLoweredStringJoinExpression join)
        => $"new global::System.Func<string>(() => {{ var {join.ValuesTemporary} = {EmitExpression(join.Values)}; var {join.SeparatorTemporary} = {EmitExpression(join.Separator)}; return global::System.String.Join(({join.SeparatorTemporary} ?? string.Empty), ({join.ValuesTemporary} ?? global::System.Array.Empty<string>())); }})()";

    private static string EmitInterpolatedString(PowerShellLoweredInterpolatedStringExpression interpolated)
    {
        var parts = interpolated.Parts.Select(part => part.Expression is null
            ? PowerShellCSharpLiteral.QuoteString(part.Text ?? string.Empty)
            : part.Expression.ClrType == typeof(string)
                ? $"({EmitExpression(part.Expression)} ?? string.Empty)"
                : $"(global::System.Convert.ToString((object?)({EmitExpression(part.Expression)}), global::System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty)").ToArray();
        return parts.Length switch
        {
            0 => "string.Empty",
            1 => parts[0],
            _ => $"global::System.String.Concat(new string[] {{ {string.Join(", ", parts)} }})"
        };
    }

    private static string EmitIndex(PowerShellLoweredIndexExpression index)
    {
        var target = EmitExpression(index.Target);
        var key = EmitExpression(index.Index);
        if (index.Kind == PowerShellBoundIndexKind.StringDictionary)
            return $"({target} is null ? null : {target}.ContainsKey({key}) ? {target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.OrderedStringDictionary)
            return $"({target} is null ? null : {target}.Contains({key}) ? (string?){target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.ObjectDictionary)
            return $"({target} is null ? null : {target}.Contains({key}) ? {target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.List)
        {
            var checkedList = index.UsePowerShellRuntimeErrors
                ? $"((global::System.Collections.IList?)({target}) ?? throw new global::System.Management.Automation.RuntimeException(\"Cannot index into a null array.\"))"
                : $"((global::System.Collections.IList?)({target}) ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
            var listIndex = $"(({key}) < 0 ? {checkedList}.Count + ({key}) : ({key}))";
            return $"({listIndex} < 0 || {listIndex} >= {checkedList}.Count ? null : {checkedList}[{listIndex}])";
        }
        if (index.Kind == PowerShellBoundIndexKind.String) target = $"({target} ?? string.Empty)";
        else target = index.UsePowerShellRuntimeErrors
            ? $"({target} ?? throw new global::System.Management.Automation.RuntimeException(\"Cannot index into a null array.\"))"
            : $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({key}) < 0 ? {target}.Length + ({key}) : ({key}))";
        return $"({normalized} < 0 || {normalized} >= {target}.Length ? null : (object){target}[{normalized}])";
    }

    private static string EmitIndexAssignment(PowerShellLoweredIndexAssignmentStatement assignment)
    {
        var target = EmitExpression(assignment.Target);
        var index = EmitExpression(assignment.Index);
        var value = EmitExpression(assignment.Value);
        if (assignment.Kind == PowerShellBoundIndexKind.List)
        {
            var checkedList = assignment.UsePowerShellRuntimeErrors
                ? $"((global::System.Collections.IList?)({target}) ?? throw new global::System.Management.Automation.RuntimeException(\"Cannot index into a null array.\"))"
                : $"((global::System.Collections.IList?)({target}) ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
            var normalizedListIndex = $"(({index}) < 0 ? {checkedList}.Count + ({index}) : ({index}))";
            var listIndexException = assignment.UsePowerShellRuntimeErrors
                ? "new global::System.Management.Automation.RuntimeException(\"Index was outside the bounds of the array.\")"
                : "new global::System.IndexOutOfRangeException(\"Index was outside the bounds of the array.\")";
            return $"{checkedList}[({normalizedListIndex} >= 0 && {normalizedListIndex} < {checkedList}.Count ? {normalizedListIndex} : throw {listIndexException})] = {value}";
        }
        if (assignment.Kind != PowerShellBoundIndexKind.Array)
            return $"{target}[{index}] = {value}";
        var checkedTarget = assignment.UsePowerShellRuntimeErrors
            ? $"({target} ?? throw new global::System.Management.Automation.RuntimeException(\"Cannot index into a null array.\"))"
            : $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({index}) < 0 ? {checkedTarget}.Length + ({index}) : ({index}))";
        var indexException = assignment.UsePowerShellRuntimeErrors
            ? "new global::System.Management.Automation.RuntimeException(\"Index was outside the bounds of the array.\")"
            : "new global::System.IndexOutOfRangeException(\"Index was outside the bounds of the array.\")";
        var checkedIndex = $"({normalized} >= 0 && {normalized} < {checkedTarget}.Length ? {normalized} : throw {indexException})";
        return $"{checkedTarget}[{checkedIndex}] = {value}";
    }

    private static string EmitClrMemberAssignment(PowerShellLoweredClrMemberAssignmentStatement assignment)
    {
        if (assignment.Receiver is null)
            return $"{PowerShellCSharpSymbolRenderer.TypeName(assignment.DeclaringType)}.{assignment.MemberName} = {EmitExpression(assignment.Value)}";
        var receiver = EmitExpression(assignment.Receiver);
        if (assignment.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellAdapterAddNoteProperty)
        {
            var name = PowerShellCSharpLiteral.QuoteString(assignment.MemberName);
            return $"global::System.Management.Automation.PSObject.AsPSObject((object?)({receiver}) ?? throw new global::System.Management.Automation.RuntimeException(\"Cannot add a member to a null value.\")).Properties.Add(new global::System.Management.Automation.PSNoteProperty({name}, {EmitExpression(assignment.Value)}))";
        }
        if (assignment.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellAdapter &&
            typeof(global::System.Collections.IDictionary).IsAssignableFrom(assignment.DeclaringType))
        {
            var name = PowerShellCSharpLiteral.QuoteString(assignment.MemberName);
            return $"((global::System.Collections.IDictionary)({receiver}))[{name}] = {EmitExpression(assignment.Value)}";
        }
        if (assignment.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellAdapter &&
            assignment.DeclaringType == typeof(global::System.Management.Automation.PSObject))
        {
            var name = PowerShellCSharpLiteral.QuoteString(assignment.MemberName);
            var message = PowerShellCSharpLiteral.QuoteString($"The property '{assignment.MemberName}' cannot be found on this object. Verify that the property exists and can be set.");
            return $"(global::System.Management.Automation.PSObject.AsPSObject((object?)({receiver}) ?? throw new global::System.Management.Automation.RuntimeException({message})).Properties[{name}] ?? throw new global::System.Management.Automation.RuntimeException({message})).Value = {EmitExpression(assignment.Value)}";
        }
        if (assignment.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellRuntimeException)
        {
            var message = PowerShellCSharpLiteral.QuoteString($"The property '{assignment.MemberName}' cannot be found on this object. Verify that the property exists and can be set.");
            receiver = $"({receiver} ?? throw new global::System.Management.Automation.RuntimeException({message}))";
        }
        else receiver = $"({receiver})";
        return $"{receiver}.{assignment.MemberName} = {EmitExpression(assignment.Value)}";
    }

    private static string EmitClrMember(PowerShellLoweredClrMemberExpression member)
    {
        if (member.IsStatic)
            return $"{PowerShellCSharpSymbolRenderer.TypeName(member.DeclaringType)}.{member.MemberName}";
        if (member.Receiver is null) throw new InvalidOperationException("Instance CLR member has no lowered receiver.");
        var receiver = EmitExpression(member.Receiver);
        return member.ReceiverBehavior switch
        {
            PowerShellClrReceiverBehavior.NormalizeNullString => $"({receiver} ?? string.Empty).{member.MemberName}",
            PowerShellClrReceiverBehavior.NormalizeNullArrayLength =>
                $"({receiver} ?? global::System.Array.Empty<{PowerShellCSharpSymbolRenderer.TypeName(member.DeclaringType.GetElementType()!)}>()).{member.MemberName}",
            PowerShellClrReceiverBehavior.NormalizeNullCount => $"(({receiver})?.{member.MemberName} ?? 0)",
            PowerShellClrReceiverBehavior.PropagateNull => $"({receiver})?.{member.MemberName}",
            PowerShellClrReceiverBehavior.DictionaryKeyLookup => EmitDictionaryKeyLookup(member, receiver, hasClrFallback: false),
            PowerShellClrReceiverBehavior.DictionaryKeyLookupWithClrFallback => EmitDictionaryKeyLookup(member, receiver, hasClrFallback: true),
            PowerShellClrReceiverBehavior.PowerShellAdapter when member.MemberName.Equals("Count", StringComparison.OrdinalIgnoreCase) && member.ClrType == typeof(int) =>
                $"new global::System.Func<int>(() => {{ var __pf_adapted_value = (object?)({receiver}); if (__pf_adapted_value is null) return 0; var __pf_count = global::System.Management.Automation.PSObject.AsPSObject(__pf_adapted_value).Properties[\"Count\"]?.Value; return __pf_count is null ? 1 : (int)global::System.Management.Automation.LanguagePrimitives.ConvertTo(__pf_count, typeof(int), global::System.Globalization.CultureInfo.InvariantCulture)!; }})()",
            PowerShellClrReceiverBehavior.PowerShellAdapter when member.ClrType != typeof(object) =>
                $"new global::System.Func<{PowerShellCSharpSymbolRenderer.TypeName(member.ClrType)}>(() => {{ var __pf_adapted_value = (object?)({receiver}); var __pf_property_value = __pf_adapted_value is null ? null : global::System.Management.Automation.PSObject.AsPSObject(__pf_adapted_value).Properties[{PowerShellCSharpLiteral.QuoteString(member.MemberName)}]?.Value; return ({PowerShellCSharpSymbolRenderer.TypeName(member.ClrType)})global::System.Management.Automation.LanguagePrimitives.ConvertTo(__pf_property_value, typeof({PowerShellCSharpSymbolRenderer.TypeName(member.ClrType)}), global::System.Globalization.CultureInfo.InvariantCulture)!; }})()",
            PowerShellClrReceiverBehavior.PowerShellAdapter =>
                $"new global::System.Func<object?>(() => {{ var __pf_adapted_value = (object?)({receiver}); return __pf_adapted_value is null ? null : global::System.Management.Automation.PSObject.AsPSObject(__pf_adapted_value).Properties[{PowerShellCSharpLiteral.QuoteString(member.MemberName)}]?.Value; }})()",
            _ => $"({receiver}).{member.MemberName}"
        };
    }

    private static string EmitDictionaryKeyLookup(PowerShellLoweredClrMemberExpression member, string receiver, bool hasClrFallback)
    {
        var name = PowerShellCSharpLiteral.QuoteString(member.MemberName);
        var dictionaryTemporary = member.DictionaryTemporary;
        var valueTemporary = member.ValueTemporary;
        if (dictionaryTemporary.Length == 0 || valueTemporary.Length == 0)
            throw new InvalidOperationException("Lowered dictionary member lookup is missing collision-free temporary names.");
        if (member.ClrType == typeof(string) && member.DeclaringType == typeof(Dictionary<string, string>))
            return $"new global::System.Func<string>(() => {{ var {dictionaryTemporary} = (global::System.Collections.Generic.Dictionary<string, string>?)({receiver}); return {dictionaryTemporary} is not null && {dictionaryTemporary}.TryGetValue({name}, out var {valueTemporary}) ? ({valueTemporary} ?? string.Empty) : string.Empty; }})()";
        var fallback = hasClrFallback
            ? $"(object?)(({PowerShellCSharpSymbolRenderer.TypeName(member.DeclaringType)}){dictionaryTemporary}).{member.MemberName}"
            : "null";
        return $"new global::System.Func<object?>(() => {{ var {dictionaryTemporary} = (global::System.Collections.IDictionary?)({receiver}); return {dictionaryTemporary} is null ? null : {dictionaryTemporary}.Contains({name}) ? {dictionaryTemporary}[{name}] : {fallback}; }})()";
    }

    private static string EmitClrInvocation(PowerShellLoweredClrInvocationExpression invocation)
    {
        var arguments = string.Join(", ", invocation.Arguments.Select(EmitExpression));
        if (invocation.InvocationKind == PowerShellClrInvocationKind.Constructor)
            return $"new {PowerShellCSharpSymbolRenderer.TypeName(invocation.DeclaringType)}({arguments})";
        if (invocation.InvocationKind == PowerShellClrInvocationKind.StaticMethod)
            return $"{PowerShellCSharpSymbolRenderer.TypeName(invocation.DeclaringType)}.{invocation.MemberName}({arguments})";
        if (invocation.Receiver is null) throw new InvalidOperationException("Instance CLR invocation has no lowered receiver.");
        var receiver = EmitExpression(invocation.Receiver);
        if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.NormalizeNullString)
            receiver = $"({receiver} ?? string.Empty)";
        else if (invocation.ReceiverBehavior == PowerShellClrReceiverBehavior.PowerShellRuntimeException)
            receiver = $"({receiver} ?? throw new global::System.Management.Automation.RuntimeException(\"You cannot call a method on a null-valued expression.\"))";
        else
            receiver = $"({receiver})";
        return $"{receiver}.{invocation.MemberName}({arguments})";
    }

    private static string EmitMutation(
        PowerShellSymbolId target,
        Type targetType,
        PowerShellBoundMutationOperator operation,
        PowerShellLoweredExpression? value,
        bool normalizeNullString,
        bool checkedIntegral)
    {
        var identifier = PowerShellCSharpSymbolRenderer.Identifier(target.Name);
        if (operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement or
            PowerShellBoundMutationOperator.PostIncrement or PowerShellBoundMutationOperator.PostDecrement)
        {
            var incrementSymbol = operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.PostIncrement ? "+" : "-";
            return operation is PowerShellBoundMutationOperator.Increment or PowerShellBoundMutationOperator.Decrement
                ? (incrementSymbol == "+" ? "++" : "--") + identifier
                : identifier + (incrementSymbol == "+" ? "++" : "--");
        }
        if (value is null) throw new InvalidOperationException($"Mutation '{operation}' requires a value.");
        var right = EmitExpression(value);
        if (normalizeNullString) right = $"({right} ?? string.Empty)";
        if (operation == PowerShellBoundMutationOperator.Assign) return $"{identifier} = {right}";
        var symbol = operation switch
        {
            PowerShellBoundMutationOperator.Add => "+",
            PowerShellBoundMutationOperator.Subtract => "-",
            PowerShellBoundMutationOperator.Multiply => "*",
            PowerShellBoundMutationOperator.Divide => "/",
            PowerShellBoundMutationOperator.Remainder => "%",
            _ => throw new InvalidOperationException($"Mutation '{operation}' has no C# rendering owner.")
        };
        return checkedIntegral
            ? $"{identifier} = checked(({PowerShellCSharpSymbolRenderer.TypeName(targetType)})({identifier} {symbol} {right}))"
            : $"{identifier} {symbol}= {right}";
    }

    private static string EmitBinary(PowerShellLoweredBinaryExpression expression)
    {
        var left = EmitExpression(expression.Left);
        var right = EmitExpression(expression.Right);
        if (IsNullOrderedComparison(expression.Operation))
            return EmitNullOrderedComparison(expression, left, right);
        if (expression.Operation is PowerShellBoundBinaryOperator.NullEqual or PowerShellBoundBinaryOperator.NullNotEqual)
        {
            var comparison = $"global::System.Object.ReferenceEquals({left}, {right})";
            return expression.Operation == PowerShellBoundBinaryOperator.NullNotEqual
                ? $"!({comparison})"
                : comparison;
        }
        if (expression.Operation is PowerShellBoundBinaryOperator.EqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualIgnoreCase or
            PowerShellBoundBinaryOperator.EqualCaseSensitive or PowerShellBoundBinaryOperator.NotEqualCaseSensitive)
        {
            var comparisonMode = expression.Operation is PowerShellBoundBinaryOperator.EqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualIgnoreCase
                ? "global::System.StringComparison.InvariantCultureIgnoreCase"
                : "global::System.StringComparison.InvariantCulture";
            var comparison = $"global::System.String.Equals({left}, {right}, {comparisonMode})";
            return expression.Operation is PowerShellBoundBinaryOperator.NotEqualIgnoreCase or PowerShellBoundBinaryOperator.NotEqualCaseSensitive
                ? $"!({comparison})"
                : comparison;
        }
        var symbol = expression.Operation switch
        {
            PowerShellBoundBinaryOperator.Add => "+",
            PowerShellBoundBinaryOperator.Subtract => "-",
            PowerShellBoundBinaryOperator.Multiply => "*",
            PowerShellBoundBinaryOperator.Divide => "/",
            PowerShellBoundBinaryOperator.Remainder => "%",
            PowerShellBoundBinaryOperator.Equal => "==",
            PowerShellBoundBinaryOperator.NotEqual => "!=",
            PowerShellBoundBinaryOperator.LessThan => "<",
            PowerShellBoundBinaryOperator.LessThanOrEqual => "<=",
            PowerShellBoundBinaryOperator.GreaterThan => ">",
            PowerShellBoundBinaryOperator.GreaterThanOrEqual => ">=",
            PowerShellBoundBinaryOperator.LogicalAnd => "&&",
            PowerShellBoundBinaryOperator.LogicalOr => "||",
            PowerShellBoundBinaryOperator.BitwiseAnd => "&",
            PowerShellBoundBinaryOperator.BitwiseOr => "|",
            PowerShellBoundBinaryOperator.BitwiseExclusiveOr => "^",
            PowerShellBoundBinaryOperator.ShiftLeft => "<<",
            PowerShellBoundBinaryOperator.ShiftRight => ">>",
            _ => throw new InvalidOperationException($"Lowered binary operator '{expression.Operation}' has no C# rendering owner.")
        };
        if (expression.Operation is PowerShellBoundBinaryOperator.Divide or PowerShellBoundBinaryOperator.Remainder && expression.ClrType == typeof(double))
            return $"(((double)({left})) {symbol} ((double)({right})))";
        if (expression.Operation is PowerShellBoundBinaryOperator.ShiftLeft or PowerShellBoundBinaryOperator.ShiftRight)
            right = $"(int)({right})";
        return $"({left} {symbol} {right})";
    }

    private static string EmitNullOrderedComparison(PowerShellLoweredBinaryExpression expression, string left, string right)
    {
        var underlyingType = Nullable.GetUnderlyingType(expression.Left.ClrType);
        if (underlyingType is null || Nullable.GetUnderlyingType(expression.Right.ClrType) != underlyingType ||
            expression.LeftTemporary is null || expression.RightTemporary is null)
            throw new InvalidOperationException("PowerShell null-ordered comparison lowering requires two matching nullable operands and two allocated temporaries.");

        var symbol = expression.Operation switch
        {
            PowerShellBoundBinaryOperator.NullOrderedLessThan => "<",
            PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual => "<=",
            PowerShellBoundBinaryOperator.NullOrderedGreaterThan => ">",
            PowerShellBoundBinaryOperator.NullOrderedGreaterThanOrEqual => ">=",
            _ => throw new InvalidOperationException($"Lowered binary operator '{expression.Operation}' is not a null-ordered comparison.")
        };
        var lessFamily = expression.Operation is PowerShellBoundBinaryOperator.NullOrderedLessThan or PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual;
        var inclusive = expression.Operation is PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual or PowerShellBoundBinaryOperator.NullOrderedGreaterThanOrEqual;
        var leftValue = $"{expression.LeftTemporary}.GetValueOrDefault()";
        var rightValue = $"{expression.RightTemporary}.GetValueOrDefault()";
        var zero = $"default({PowerShellCSharpSymbolRenderer.TypeName(underlyingType)})";
        var leftOnly = lessFamily ? $"{leftValue} < {zero}" : $"{leftValue} >= {zero}";
        var rightOnly = lessFamily ? $"{rightValue} >= {zero}" : $"{rightValue} < {zero}";
        var nullableType = PowerShellCSharpSymbolRenderer.TypeName(expression.Left.ClrType);
        return $"new global::System.Func<bool>(() => {{ {nullableType} {expression.LeftTemporary} = {left}; {nullableType} {expression.RightTemporary} = {right}; return {expression.LeftTemporary}.HasValue ? ({expression.RightTemporary}.HasValue ? {leftValue} {symbol} {rightValue} : {leftOnly}) : ({expression.RightTemporary}.HasValue ? {rightOnly} : {(inclusive ? "true" : "false")}); }})()";
    }

    private static bool IsNullOrderedComparison(PowerShellBoundBinaryOperator operation)
        => operation is PowerShellBoundBinaryOperator.NullOrderedLessThan or
            PowerShellBoundBinaryOperator.NullOrderedLessThanOrEqual or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThan or
            PowerShellBoundBinaryOperator.NullOrderedGreaterThanOrEqual;

    private static string EmitUnary(PowerShellLoweredUnaryExpression expression)
    {
        var symbol = expression.Operation switch
        {
            PowerShellBoundUnaryOperator.Identity => "+",
            PowerShellBoundUnaryOperator.Negate => "-",
            PowerShellBoundUnaryOperator.LogicalNot => "!",
            PowerShellBoundUnaryOperator.BitwiseNot => "~",
            _ => throw new InvalidOperationException($"Lowered unary operator '{expression.Operation}' has no C# rendering owner.")
        };
        return $"({symbol}{EmitExpression(expression.Operand)})";
    }

}

internal sealed class PowerShellBoundCSharpResult
{
    internal PowerShellBoundCSharpResult(PowerShellCSharpMethodEmission[] methods, PowerShellSemanticDiagnostic[] diagnostics)
    {
        Methods = methods;
        Diagnostics = diagnostics;
    }

    internal PowerShellCSharpMethodEmission[] Methods { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal bool Success => Methods.Length > 0 && Diagnostics.Length == 0;
}
