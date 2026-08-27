using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private void EmitAssignment(AssignmentStatementAst assignment, bool terminate)
    {
        if (assignment.Left is IndexExpressionAst index)
        {
            EmitIndexedAssignment(assignment, index, terminate);
            return;
        }
        if (assignment.Left is MemberExpressionAst member)
        {
            if (assignment.Operator.ToString() != "Equals")
                throw Error(assignment, "Typed CLR member assignment currently supports only simple '=' mutation.");
            AppendLine(_memberEmitter.EmitMemberAssignment(member, assignment.Right) + (terminate ? ";" : string.Empty));
            return;
        }

        var variable = FindAssignedVariable(assignment.Left)
            ?? throw Error(assignment.Left, "Only local-variable assignment is supported.");
        var name = variable.VariablePath.UserPath;
        var identifier = GetVariableIdentifier(name);
        var isParameter = _body.ParamBlock?.Parameters.Any(parameter =>
            parameter.Name.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase)) == true;
        var declaration = !_declaredLocals.Contains(name) && !isParameter;
        var left = declaration ? $"{GetTypeName(_variables[name])} {identifier}" : identifier;
        var operation = assignment.Operator.ToString() switch
        {
            "Equals" => "=",
            "PlusEquals" => "+=",
            "MinusEquals" => "-=",
            "MultiplyEquals" => "*=",
            "DivideEquals" => "/=",
            "RemEquals" => "%=",
            _ => throw Error(assignment, $"Assignment operator '{assignment.Operator}' is not implemented.")
        };
        var rightType = InferExpressionType(assignment.Right);
        if (operation != "=" && !PowerShellCSharpOperatorPolicy.SupportsCompoundAssignment(assignment.Operator.ToString(), _variables[name], rightType))
            throw Error(assignment, $"Compound assignment '{assignment.Operator}' is not defined for CLR types '{_variables[name].FullName}' and '{rightType.FullName}' on the conservative compilation path.");
        if (operation != "=" && IsIntegral(_variables[name]) && !_explicitlyTypedVariables.Contains(name))
            throw Error(assignment, $"Integral compound assignment to untyped local '${name}' can promote dynamically in PowerShell and is not eligible for typed compilation.");
        if (operation != "=" && IsIntegral(_variables[name]) && PowerShellRuntimeExceptionCatchPolicy.Contains(assignment))
        {
            throw Error(
                assignment,
                "Integral compound assignment inside a RuntimeException catch cannot preserve PowerShell's overflow-error wrapping on the conservative compilation path.");
        }

        _declaredLocals.Add(name);
        var suffix = terminate ? ";" : string.Empty;
        var right = EmitExpression(assignment.Right);
        if (operation == "=" && _variables[name] == typeof(string) && _explicitlyTypedVariables.Contains(name))
            right = $"({right} ?? string.Empty)";
        var expression = $"{left} {operation} {right}";
        if (operation != "=" && IsIntegral(_variables[name]))
        {
            var binaryOperation = operation.Substring(0, operation.Length - 1);
            expression = $"{identifier} = checked(({GetTypeName(_variables[name])})({identifier} {binaryOperation} {right}))";
        }
        AppendLine(expression + suffix);
    }
}
