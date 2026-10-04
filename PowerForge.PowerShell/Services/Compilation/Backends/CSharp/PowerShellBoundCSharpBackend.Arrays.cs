namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private string EmitArrayCopy(PowerShellLoweredArrayCopyExpression copy)
    {
        var source = copy.SourceTemporary;
        var result = copy.ResultTemporary;
        var index = copy.IndexTemporary;
        var empty = copy.ShareEmptyResult
            ? "global::System.Array.Empty<object>()"
            : "new object?[0]";
        return "new global::System.Func<object?[]>(() => { " +
               $"var {source} = {EmitExpression(copy.Source)}; " +
               $"if ({source} is null) return new object?[] {{ null }}; " +
               $"if ({source}.Length == 0) return {empty}; " +
               $"var {result} = new object?[{source}.Length]; " +
               $"for (int {index} = 0; {index} < {source}.Length; {index}++) {result}[{index}] = {source}[{index}]; " +
               $"return {result}; " + "})()";
    }

    private string EmitArray(PowerShellLoweredArrayExpression array)
    {
        var elementType = array.ClrType.GetElementType()!;
        if (array.Kind == PowerShellBoundArrayKind.SharedEmptyCollection) return "global::System.Array.Empty<object>()";
        if (array.Elements.Length == 0) return $"new {PowerShellCSharpSymbolRenderer.TypeName(elementType)}[] {{ }}";
        return $"new {PowerShellCSharpSymbolRenderer.TypeName(elementType)}[] {{ {string.Join(", ", array.Elements.Select(EmitExpression))} }}";
    }

    private string EmitArrayConcatenation(PowerShellLoweredArrayConcatenationExpression concatenation)
    {
        var left = EmitExpression(concatenation.Left);
        var right = EmitExpression(concatenation.Right);
        var appendRight = concatenation.EnumerateRight
            ? "if (__powerForgeRight is null) { __powerForgeValues.Add(null); } else { foreach (object? __powerForgeItem in (global::System.Collections.IEnumerable)__powerForgeRight) __powerForgeValues.Add(__powerForgeItem); }"
            : "__powerForgeValues.Add(__powerForgeRight);";
        return $"((global::System.Func<object?[]>)(() => {{ object? __powerForgeLeft = (object?)({left}); object? __powerForgeRight = (object?)({right}); var __powerForgeValues = new global::System.Collections.Generic.List<object?>(); if (__powerForgeLeft is not null) {{ foreach (object? __powerForgeItem in (global::System.Collections.IEnumerable)__powerForgeLeft) __powerForgeValues.Add(__powerForgeItem); }} {appendRight} return __powerForgeValues.ToArray(); }}))()";
    }
}
