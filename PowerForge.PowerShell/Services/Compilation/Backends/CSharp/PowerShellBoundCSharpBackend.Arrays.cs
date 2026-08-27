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
        var rightValues = concatenation.EnumerateRight
            ? "__powerForgeRight is null ? new object?[] { null } : global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__powerForgeRight)"
            : "new object?[] { __powerForgeRight }";
        return $"((global::System.Func<object?[]>)(() => {{ object? __powerForgeLeft = (object?)({left}); object? __powerForgeRight = (object?)({right}); global::System.Collections.Generic.IEnumerable<object?> __powerForgeLeftValues = __powerForgeLeft is null ? global::System.Array.Empty<object?>() : global::System.Linq.Enumerable.Cast<object?>((global::System.Collections.IEnumerable)__powerForgeLeft); global::System.Collections.Generic.IEnumerable<object?> __powerForgeRightValues = {rightValues}; return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Concat(__powerForgeLeftValues, __powerForgeRightValues)); }}))()";
    }
}
