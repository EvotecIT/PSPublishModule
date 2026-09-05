namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string EmitArray(PowerShellLoweredArrayExpression array)
    {
        var elementType = array.ClrType.GetElementType()!;
        if (array.Elements.Length == 0) return $"global::System.Array.Empty<{PowerShellCSharpSymbolRenderer.TypeName(elementType)}>()";
        return $"new {PowerShellCSharpSymbolRenderer.TypeName(elementType)}[] {{ {string.Join(", ", array.Elements.Select(EmitExpression))} }}";
    }

    private static string EmitArrayConcatenation(PowerShellLoweredArrayConcatenationExpression concatenation)
    {
        var left = EmitExpression(concatenation.Left);
        var right = EmitExpression(concatenation.Right);
        var appendRight = concatenation.EnumerateRight
            ? "if (__powerForgeRight is null) { __powerForgeValues.Add(null); } else { foreach (object? __powerForgeItem in (global::System.Collections.IEnumerable)__powerForgeRight) __powerForgeValues.Add(__powerForgeItem); }"
            : "__powerForgeValues.Add(__powerForgeRight);";
        return $"((global::System.Func<object?[]>)(() => {{ object? __powerForgeLeft = (object?)({left}); object? __powerForgeRight = (object?)({right}); var __powerForgeValues = new global::System.Collections.Generic.List<object?>(); if (__powerForgeLeft is not null) {{ foreach (object? __powerForgeItem in (global::System.Collections.IEnumerable)__powerForgeLeft) __powerForgeValues.Add(__powerForgeItem); }} {appendRight} return __powerForgeValues.ToArray(); }}))()";
    }
}
