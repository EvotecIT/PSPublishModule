using System.Management.Automation.Language;
using System.Reflection;

namespace PowerForge;

/// <summary>Consumes native variable-flow annotations for PowerShell-hosted function storage.</summary>
/// <remarks>No authored body is emitted or evaluated by this analysis.</remarks>
internal static class PowerShellNativeVariableAnalysis
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type MetadataProvider = typeof(Ast).Assembly.GetType(
        "System.Management.Automation.Language.IParameterMetadataProvider", throwOnError: true)!;
    private static readonly MethodInfo AnalyzeMethod = typeof(Ast).Assembly.GetType(
        "System.Management.Automation.Language.VariableAnalysis", throwOnError: true)!
        .GetMethod("Analyze", Flags, null, new[] { MetadataProvider, typeof(bool), typeof(bool) }, null)
        ?? throw new NotSupportedException("PowerShell's native variable-flow analysis is unavailable.");
    private static readonly PropertyInfo TupleIndex = typeof(VariableExpressionAst).GetProperty("TupleIndex", Flags)!;
    private static readonly PropertyInfo Automatic = typeof(VariableExpressionAst).GetProperty("Automatic", Flags)!;

    internal static string[] Analyze(FunctionDefinitionAst function)
    {
        var usesCmdletBinding = (bool)MetadataProvider.GetMethod("UsesCmdletBinding")!.Invoke(function.Body, null)!;
        var analysis = (Tuple<Type, Dictionary<string, int>>)AnalyzeMethod.Invoke(null, new object[] { function.Body, false, usesCmdletBinding })!;
        var parameters = PowerShellParameterSyntax.GetParameters(function.Body)
            .Select(static parameter => parameter.Name.VariablePath.UserPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var locals = function.Body.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: false)
            .Cast<VariableExpressionAst>().Where(IsDirectLocal)
            .Select(static variable => variable.VariablePath.UserPath)
            .Select(static name => name.StartsWith("local:", StringComparison.OrdinalIgnoreCase) ? name.Substring(6) : name)
            .Where(name => !parameters.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Iterator storage can be implicit: a foreach statement need not contain
        // an authored read of $foreach for native analysis to allocate its slot.
        if (analysis.Item2.ContainsKey("foreach") && !parameters.Contains("foreach") &&
            !locals.Contains("foreach", StringComparer.OrdinalIgnoreCase)) locals.Add("foreach");
        return locals.ToArray();
    }

    internal static bool IsDirectLocal(VariableExpressionAst variable)
        => (int)TupleIndex.GetValue(variable)! >= 0 && !(bool)Automatic.GetValue(variable)!;

    /// <summary>Retains the authored type metadata needed by the already-analyzed optimized slots.</summary>
    internal static string[] FindLocalTypeDeclarations(FunctionDefinitionAst function, IEnumerable<string> localNames)
    {
        var names = localNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return function.Body.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false)
            .Cast<AssignmentStatementAst>().Where(assignment => assignment.Left is AttributedExpressionAst &&
                PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } variable &&
                IsDirectLocal(variable) && names.Contains((variable.VariablePath.IsLocal ? variable.VariablePath.UserPath.Substring(6) : variable.VariablePath.UserPath)))
            .Select(static assignment => assignment.Left.Extent.Text).Distinct(StringComparer.Ordinal).ToArray();
    }
}
