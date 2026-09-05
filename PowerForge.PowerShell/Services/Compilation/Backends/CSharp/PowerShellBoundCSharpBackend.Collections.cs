namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitIndex(PowerShellLoweredIndexExpression index)
    {
        var target = index.TargetTemporary;
        var key = index.IndexTemporary;
        var body = EmitIndexBody(index, target, key);
        var returnType = PowerShellCSharpSymbolRenderer.TypeName(index.ClrType) +
                         (!index.ClrType.IsValueType ? "?" : string.Empty);
        return $"new global::System.Func<{returnType}>(() => {{ var {target} = {EmitExpression(index.Target)}; var {key} = {EmitExpression(index.Index)}; return {body}; }})()";
    }

    private static string EmitIndexBody(PowerShellLoweredIndexExpression index, string target, string key)
    {
        if (index.Kind == PowerShellBoundIndexKind.StringDictionary)
            return $"({target} is null ? null : {target}.ContainsKey({key}) ? {target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.OrderedStringDictionary)
            return $"({target} is null ? null : {target}.Contains({key}) ? (string?){target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.ObjectDictionary)
            return $"({target} is null ? null : {target}.Contains({key}) ? {target}[{key}] : null)";
        if (index.Kind == PowerShellBoundIndexKind.List)
        {
            var nullArrayException = EmitPowerShellRuntimeException(
                "Cannot index into a null array.",
                "NullArray",
                "InvalidOperation");
            var checkedList = index.UsePowerShellRuntimeErrors
                ? $"((global::System.Collections.IList?)({target}) ?? throw {nullArrayException})"
                : $"((global::System.Collections.IList?)({target}) ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
            var listIndex = $"(({key}) < 0 ? {checkedList}.Count + ({key}) : ({key}))";
            return $"({listIndex} < 0 || {listIndex} >= {checkedList}.Count ? null : {checkedList}[{listIndex}])";
        }
        if (index.Kind == PowerShellBoundIndexKind.String) target = $"({target} ?? string.Empty)";
        else target = index.UsePowerShellRuntimeErrors
            ? $"({target} ?? throw {EmitPowerShellRuntimeException("Cannot index into a null array.", "NullArray", "InvalidOperation")})"
            : $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({key}) < 0 ? {target}.Length + ({key}) : ({key}))";
        return $"({normalized} < 0 || {normalized} >= {target}.Length ? null : (object){target}[{normalized}])";
    }

    private static string EmitIndexAssignment(PowerShellLoweredIndexAssignmentStatement assignment)
    {
        var target = assignment.TargetTemporary;
        var index = assignment.IndexTemporary;
        var value = assignment.ValueTemporary;
        var body = EmitIndexAssignmentBody(assignment, target, index, value);
        var valueType = PowerShellCSharpSymbolRenderer.TypeName(assignment.Value.ClrType) +
                        (!assignment.Value.ClrType.IsValueType ? "?" : string.Empty);
        return "new global::System.Action(() => { " +
               $"{valueType} {value} = {EmitExpression(assignment.Value)}; " +
               $"var {target} = {EmitExpression(assignment.Target)}; " +
               $"var {index} = {EmitExpression(assignment.Index)}; " +
               body + "; })()";
    }

    private static string EmitIndexAssignmentBody(
        PowerShellLoweredIndexAssignmentStatement assignment,
        string target,
        string index,
        string value)
    {
        if (assignment.Kind == PowerShellBoundIndexKind.List)
        {
            var nullArrayException = EmitPowerShellRuntimeException(
                "Cannot index into a null array.",
                "NullArray",
                "InvalidOperation");
            var checkedList = assignment.UsePowerShellRuntimeErrors
                ? $"((global::System.Collections.IList?)({target}) ?? throw {nullArrayException})"
                : $"((global::System.Collections.IList?)({target}) ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
            var normalizedListIndex = $"(({index}) < 0 ? {checkedList}.Count + ({index}) : ({index}))";
            const string rawListIndexException =
                "new global::System.ArgumentOutOfRangeException(\"index\", \"Index was out of range.\")";
            var listIndexException = assignment.UsePowerShellRuntimeErrors
                ? EmitPowerShellRuntimeException(
                    "Index was out of range.",
                    "System.ArgumentOutOfRangeException",
                    "OperationStopped",
                    rawListIndexException)
                : rawListIndexException;
            return $"{checkedList}[({normalizedListIndex} >= 0 && {normalizedListIndex} < {checkedList}.Count ? {normalizedListIndex} : throw {listIndexException})] = {value}";
        }
        if (assignment.Kind != PowerShellBoundIndexKind.Array)
            return $"{target}[{index}] = {value}";
        var checkedTarget = assignment.UsePowerShellRuntimeErrors
            ? $"({target} ?? throw {EmitPowerShellRuntimeException("Cannot index into a null array.", "NullArray", "InvalidOperation")})"
            : $"({target} ?? throw new global::System.InvalidOperationException(\"Cannot index into a null array.\"))";
        var normalized = $"(({index}) < 0 ? {checkedTarget}.Length + ({index}) : ({index}))";
        const string rawIndexException =
            "new global::System.IndexOutOfRangeException(\"Index was outside the bounds of the array.\")";
        var indexException = assignment.UsePowerShellRuntimeErrors
            ? EmitPowerShellRuntimeException(
                "Index was outside the bounds of the array.",
                "System.IndexOutOfRangeException",
                "OperationStopped",
                rawIndexException)
            : rawIndexException;
        var checkedIndex = $"({normalized} >= 0 && {normalized} < {checkedTarget}.Length ? {normalized} : throw {indexException})";
        return $"{checkedTarget}[{checkedIndex}] = {value}";
    }

    private static string EmitPowerShellRuntimeException(
        string message,
        string errorId,
        string category,
        string? errorException = null)
    {
        var quotedMessage = PowerShellCSharpLiteral.QuoteString(message);
        errorException ??= "new global::System.Management.Automation.RuntimeException(" + quotedMessage + ")";
        return "new global::System.Management.Automation.RuntimeException(" + quotedMessage +
               ", null, new global::System.Management.Automation.ErrorRecord(" +
               errorException + ", " +
               PowerShellCSharpLiteral.QuoteString(errorId) +
               ", global::System.Management.Automation.ErrorCategory." + category + ", null))";
    }
}
