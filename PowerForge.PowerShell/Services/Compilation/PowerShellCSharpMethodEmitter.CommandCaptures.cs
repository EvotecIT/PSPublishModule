using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private void InferCapturedRuntimeAssignmentType(AssignmentStatementAst assignment, VariableExpressionAst variable)
    {
        var targetType = ((ConvertExpressionAst)assignment.Left).StaticType;
        var name = variable.VariablePath.UserPath;
        if (targetType == typeof(void) ||
            !PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, _targetFramework, _capabilities))
            throw Error(assignment.Left, $"Captured PowerShell output target '${name}' uses unsupported CLR type '{targetType.FullName}'.");
        if (_variables.TryGetValue(name, out var existingType))
        {
            if (existingType != targetType)
                throw Error(assignment.Left, $"Captured PowerShell output changes '${name}' from '{existingType.FullName}' to '{targetType.FullName}'.");
            _explicitlyTypedVariables.Add(name);
            return;
        }

        _variables.Add(name, targetType);
        AddVariableIdentifier(name, variable);
        _firstAssignmentOffsets.Add(name, assignment.Extent.StartOffset);
        _explicitlyTypedVariables.Add(name);
    }

    private void EmitCapturedRuntimeAssignment(AssignmentStatementAst assignment)
    {
        var variable = (VariableExpressionAst)((ConvertExpressionAst)assignment.Left).Child;
        var name = variable.VariablePath.UserPath;
        var referencedNames = assignment.Right
            .FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .OfType<VariableExpressionAst>()
            .Select(static reference => reference.VariablePath.UserPath)
            .Where(reference => _variables.ContainsKey(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var switchParameters = _body.ParamBlock?.Parameters
            .Where(static parameter => parameter.StaticType == typeof(System.Management.Automation.SwitchParameter))
            .Select(static parameter => parameter.Name.VariablePath.UserPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameterBlock = "param(" + string.Join(", ", referencedNames.Select(reference =>
            (switchParameters.Contains(reference) ? "[switch] " : string.Empty) + EmitBracedPowerShellVariable(reference))) + ")";
        var script = parameterBlock + Environment.NewLine + assignment.Right.Extent.Text;
        var arguments = string.Join(", ", referencedNames.Select(GetVariableIdentifier));
        var targetType = _variables[name];
        var targetName = GetTypeName(targetType);
        var capture = $"__invokePowerShellCapture({PowerShellCSharpLiteral.QuoteString(script)}, new object?[] {{ {arguments} }})";
        var converted = $"({targetName})global::System.Management.Automation.LanguagePrimitives.ConvertTo({capture}, typeof({targetName}), global::System.Globalization.CultureInfo.InvariantCulture)!";
        if (targetType == typeof(string))
            converted = $"({converted} ?? string.Empty)";

        var isParameter = _body.ParamBlock?.Parameters.Any(parameter =>
            parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
        var declaration = !_declaredLocals.Contains(name) && !isParameter;
        var left = declaration ? $"{targetName} {GetVariableIdentifier(name)}" : GetVariableIdentifier(name);
        AppendLine($"{left} = {converted};");
        _declaredLocals.Add(name);
    }
}
