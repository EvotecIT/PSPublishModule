namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    private static bool ReturnsCompilerDictionary(PowerShellBoundFunction function)
    {
        var statements = EnumerateStatements(function.Body).ToArray();
        var dictionaryLocals = statements
            .OfType<PowerShellBoundAssignmentStatement>()
            .Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
            .Select(static assignment => assignment.Target.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        var nativeDictionaryLocals = statements
            .OfType<PowerShellBoundNativeAssignmentStatement>()
            .Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
            .Select(static assignment => assignment.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regionDictionaryInputs = function.Parameters
            .Where(static parameter => parameter.Symbol.Kind == PowerShellSymbolKind.Local &&
                                       PowerShellRegionTransferTypePolicy.IsAtomicDictionaryReference(parameter.Type.ClrType))
            .Select(static parameter => parameter.Symbol.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        return statements
            .Select(GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .Any(expression => CarriesCompilerDictionary(expression!, dictionaryLocals, nativeDictionaryLocals, regionDictionaryInputs));
    }

    private static bool HasQualifiedNativeDictionaryOutput(PowerShellBoundFunction function)
    {
        if (function.NativeFunctionBinding is null) return false;
        var statements = EnumerateStatements(function.Body).ToArray();
        var assignments = statements.OfType<PowerShellBoundAssignmentStatement>().ToArray();
        var nativeAssignments = statements.OfType<PowerShellBoundNativeAssignmentStatement>().ToArray();
        var dictionaryLocals = assignments
            .Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
            .Select(static assignment => assignment.Target.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        var nativeDictionaryLocals = nativeAssignments
            .Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
            .Select(static assignment => assignment.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var qualifiedLocals = assignments.GroupBy(static assignment => assignment.Target.StableKey, StringComparer.Ordinal)
            .Where(static group => group.All(static assignment =>
                assignment.Value is PowerShellBoundDictionaryExpression dictionary && IsHostLiteralMapTree(dictionary)))
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        var qualifiedNativeLocals = nativeAssignments.GroupBy(static assignment => assignment.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Any(static assignment => assignment.Value is PowerShellBoundDictionaryExpression) &&
                                   group.Where(static assignment => assignment.Value is PowerShellBoundDictionaryExpression)
                                       .All(static assignment => IsHostLiteralMapTree((PowerShellBoundDictionaryExpression)assignment.Value)))
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regionDictionaryInputs = function.Parameters
            .Where(static parameter => parameter.Symbol.Kind == PowerShellSymbolKind.Local &&
                                       PowerShellRegionTransferTypePolicy.IsAtomicDictionaryReference(parameter.Type.ClrType))
            .Select(static parameter => parameter.Symbol.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        return statements
            .Select(GetSuccessOutputExpression)
            .Where(static expression => expression is not null)
            .All(expression => !CarriesCompilerDictionary(expression!, dictionaryLocals, nativeDictionaryLocals, regionDictionaryInputs) ||
                               IsQualifiedNativeDictionaryOutput(expression!, qualifiedLocals, qualifiedNativeLocals));
    }

    private static bool IsQualifiedNativeDictionaryOutput(PowerShellBoundExpression expression,
        ISet<string> qualifiedLocals, ISet<string> qualifiedNativeLocals)
        => expression switch
        {
            PowerShellBoundDictionaryExpression dictionary => IsHostLiteralMapTree(dictionary),
            PowerShellBoundVariableExpression variable => qualifiedLocals.Contains(variable.Symbol.StableKey),
            PowerShellBoundNativeVariableExpression variable => qualifiedNativeLocals.Contains(variable.Name),
            PowerShellBoundConversionExpression conversion when conversion.Type.ClrType == typeof(object) =>
                IsQualifiedNativeDictionaryOutput(conversion.Operand, qualifiedLocals, qualifiedNativeLocals),
            _ => false
        };

    private static bool IsHostLiteralMapTree(PowerShellBoundDictionaryExpression dictionary)
        => (dictionary.Kind is PowerShellBoundDictionaryKind.StringHashtable or PowerShellBoundDictionaryKind.ObjectDictionary or
            PowerShellBoundDictionaryKind.OrderedStringDictionary or PowerShellBoundDictionaryKind.OrderedObjectDictionary) &&
           dictionary.Entries.All(static entry =>
               entry.Value is not PowerShellBoundDictionaryExpression nested || IsHostLiteralMapTree(nested));

    private static bool CarriesCompilerDictionary(
        PowerShellBoundExpression expression,
        ISet<string> dictionaryLocals,
        ISet<string> nativeDictionaryLocals,
        ISet<string> regionDictionaryInputs)
    {
        var exactInputPassthrough = expression is PowerShellBoundVariableExpression input &&
                                    regionDictionaryInputs.Contains(input.Symbol.StableKey) &&
                                    !dictionaryLocals.Contains(input.Symbol.StableKey);
        return expression is PowerShellBoundDictionaryExpression ||
           (!exactInputPassthrough && expression.Type.DictionaryValueKind != PowerShellDictionaryValueKind.None) ||
           expression is PowerShellBoundVariableExpression variable && dictionaryLocals.Contains(variable.Symbol.StableKey) ||
           expression is PowerShellBoundNativeVariableExpression native && nativeDictionaryLocals.Contains(native.Name) ||
           expression is PowerShellBoundArrayExpression array && array.Elements.Any(element =>
               CarriesCompilerDictionary(element, dictionaryLocals, nativeDictionaryLocals, regionDictionaryInputs)) ||
           expression is PowerShellBoundConversionExpression conversion && conversion.Type.ClrType == typeof(object) &&
               CarriesCompilerDictionary(conversion.Operand, dictionaryLocals, nativeDictionaryLocals, regionDictionaryInputs);
    }
}
