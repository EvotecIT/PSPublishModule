using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private Type InferArrayLiteralType(ArrayLiteralAst array)
    {
        if (array.Elements.Count == 0)
            throw Error(array, "Empty array literals require an explicit element type.");

        var contextualType = GetContextualArrayType(array);
        if (contextualType is null)
            return typeof(object[]);

        EnsureArrayElementsAssignable(array.Elements, contextualType.GetElementType()!, array);
        return contextualType;
    }

    private string EmitArray(ArrayLiteralAst array)
    {
        var arrayType = InferArrayLiteralType(array);
        var elementType = arrayType.GetElementType()!;
        return $"new {GetTypeName(elementType)}[] {{ {string.Join(", ", array.Elements.Select(EmitExpression))} }}";
    }

    private Type InferArrayExpressionType(ArrayExpressionAst array)
    {
        var elements = GetArrayExpressionElements(array);
        var contextualType = GetContextualArrayType(array);
        if (elements.Length == 0)
            return contextualType
                   ?? throw Error(array, "Empty @() requires an explicit one-dimensional array type on its assignment target.");
        if (elements.Any(element => InferExpressionType(element).IsArray || IsNullExpression(element)))
            throw Error(array, "Typed @() expressions do not accept array-valued or null pipeline output; use Hybrid fallback for PowerShell enumeration and null-suppression semantics.");
        if (contextualType is null)
            return typeof(object[]);

        EnsureArrayElementsAssignable(elements, contextualType.GetElementType()!, array);
        return contextualType;
    }

    private string EmitArrayExpression(ArrayExpressionAst array)
    {
        var arrayType = InferArrayExpressionType(array);
        var elementType = arrayType.GetElementType()!;
        var elements = GetArrayExpressionElements(array);
        return elements.Length == 0
            ? $"global::System.Array.Empty<{GetTypeName(elementType)}>()"
            : $"new {GetTypeName(elementType)}[] {{ {string.Join(", ", elements.Select(EmitExpression))} }}";
    }

    private ExpressionAst[] GetArrayExpressionElements(ArrayExpressionAst array)
    {
        var result = new List<ExpressionAst>();
        foreach (var statement in array.SubExpression.Statements)
        {
            if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                pipeline.PipelineElements[0] is not CommandExpressionAst commandExpression)
                throw Error(statement, "Typed @() expressions accept only side-effect-free expression statements.");
            var expression = commandExpression.Expression;
            if (expression is ArrayLiteralAst literal)
                result.AddRange(literal.Elements);
            else
                result.Add(expression);
        }
        return result.ToArray();
    }

    private void EnsureArrayElementsAssignable(IEnumerable<ExpressionAst> elements, Type elementType, Ast array)
    {
        var incompatible = elements.FirstOrDefault(element => !CanAssign(elementType, InferExpressionType(element)));
        if (incompatible is not null)
            throw Error(array, $"Array element type '{InferExpressionType(incompatible).FullName}' cannot be assigned to explicit element type '{elementType.FullName}' without PowerShell runtime conversion.");
    }

    private static Type? GetContextualArrayType(Ast array)
    {
        for (Ast? current = array.Parent; current is not null; current = current.Parent)
        {
            if (current is AssignmentStatementAst assignment)
                return assignment.Left is ConvertExpressionAst conversion &&
                       conversion.StaticType.IsArray &&
                       conversion.StaticType.GetArrayRank() == 1
                    ? conversion.StaticType
                    : null;
            if (current is StatementBlockAst)
                break;
        }
        return null;
    }
}
