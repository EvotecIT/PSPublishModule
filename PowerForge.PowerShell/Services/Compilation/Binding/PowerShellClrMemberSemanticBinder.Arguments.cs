using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

internal static partial class PowerShellClrMemberSemanticBinder
{
    private static MethodBase? SelectBest<TMember>(
        IEnumerable<TMember> candidates,
        PowerShellBoundExpression[] arguments,
        ExpressionAst[] argumentSyntax,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        SourceSpan span,
        string description,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
        where TMember : MethodBase
    {
        var candidatesWithParameters = candidates
            .Select(candidate => new { Candidate = (MethodBase)candidate, Parameters = candidate.GetParameters() }).ToArray();
        var shapes = candidatesWithParameters.Where(match => match.Parameters.Length == arguments.Length).ToArray();
        var numericUnion = arguments.Any(static argument => argument.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble);
        // Object is the storage representation, not the dynamic type used by
        // PowerShell overload selection. Until variant dispatch is qualified,
        // require one authored-arity shape, including otherwise unsupported shapes.
        if (numericUnion && candidatesWithParameters.Count(match => CouldAcceptArgumentCount(match.Parameters, arguments.Length)) != 1)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2623",
                $"CLR invocation of {description} with a numeric-union argument requires one unambiguous arity shape; overload dispatch remains on the PowerShell runtime path.", span));
            return null;
        }
        var matches = shapes
            .Where(match => !match.Candidate.IsGenericMethodDefinition && !match.Candidate.ContainsGenericParameters &&
                            IsSupportedMember(match.Candidate, targetFramework) &&
                            match.Parameters.All(static parameter => !parameter.ParameterType.IsByRef && !parameter.IsOut) &&
                            !match.Parameters.Any(static parameter => parameter.GetCustomAttribute<ParamArrayAttribute>() is not null))
            .Select(match => new { match.Candidate, Score = ScoreArguments(arguments, argumentSyntax, match.Parameters, capabilities) })
            .Where(static match => match.Score >= 0)
            .OrderBy(static match => match.Score)
            .ThenBy(static match => match.Candidate.ToString(), StringComparer.Ordinal)
            .ToArray();
        if (matches.Length == 0)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2609", $"No exact CLR overload was found for {description} with the bound argument types.", span));
            return null;
        }
        if (matches.Length > 1 && matches[0].Score == matches[1].Score)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2610", $"CLR overload resolution for {description} is ambiguous on the conservative typed path.", span));
            return null;
        }
        return matches[0].Candidate;
    }

    private static bool CouldAcceptArgumentCount(ParameterInfo[] parameters, int count)
    {
        var variadic = parameters.Length != 0 && parameters[parameters.Length - 1].GetCustomAttribute<ParamArrayAttribute>() is not null;
        if (count > parameters.Length) return variadic;
        return parameters.Skip(count).All(parameter => parameter.IsOptional || parameter.GetCustomAttribute<ParamArrayAttribute>() is not null);
    }

    private static int ScoreArguments(PowerShellBoundExpression[] arguments, ExpressionAst[] syntax, ParameterInfo[] parameters,
        PowerShellCompilationCapability capabilities)
    {
        var score = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var source = arguments[index].Type.ClrType;
            var target = parameters[index].ParameterType;
            if (arguments[index].Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble)
            {
                if (target == typeof(object)) continue;
                if (target == typeof(int) && capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors))
                {
                    score += 4;
                    continue;
                }
                return -1;
            }
            if (source == target) continue;
            if (target.IsAssignableFrom(source)) { score += 1; continue; }
            if (PowerShellClrTypeSemantics.CanAssign(target, source)) { score += 2; continue; }
            if (target == typeof(char) && syntax[index] is StringConstantExpressionAst text && text.Value.Length == 1) { score += 3; continue; }
            if (target.IsEnum && syntax[index] is StringConstantExpressionAst enumText && TryResolveEnumLiteral(target, enumText.Value, out _)) { score += 3; continue; }
            return -1;
        }
        return score;
    }

    private static PowerShellClrArgumentConversion[] CreateArgumentConversions(PowerShellBoundExpression[] arguments, ParameterInfo[] parameters)
        => arguments.Select((argument, index) =>
            argument.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble && parameters[index].ParameterType == typeof(int)
                ? new PowerShellClrArgumentConversion(PowerShellClrArgumentConversionKind.Int32OrDoubleToInt32, parameters[index].Name ?? string.Empty)
                : default).ToArray();

    private static PowerShellBoundExpression NormalizeLiteralArgument(PowerShellBoundExpression argument, ExpressionAst syntax, Type targetType)
    {
        if (targetType == typeof(char) && syntax is StringConstantExpressionAst text && text.Value.Length == 1)
            return new PowerShellBoundLiteralExpression(argument.Span, text.Value[0], new PowerShellTypeFact(typeof(char), PowerShellTypeFactProvenance.Literal, "A one-character literal binds to the selected Char parameter."), PowerShellValueState.Known);
        if (targetType.IsEnum && syntax is StringConstantExpressionAst enumText && TryResolveEnumLiteral(targetType, enumText.Value, out var value))
            return new PowerShellBoundLiteralExpression(argument.Span, value, new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Literal, "A named enum literal binds to the selected enum parameter."), PowerShellValueState.Known);
        return argument;
    }
}
