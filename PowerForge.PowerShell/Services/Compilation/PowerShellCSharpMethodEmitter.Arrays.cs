using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellCSharpMethodEmitter
{
    private Type InferArrayExpressionType(ArrayExpressionAst array)
    {
        var elements = GetArrayExpressionElements(array);
        if (elements.Length == 0)
            return GetContextualArrayType(array)
                   ?? throw Error(array, "Empty @() requires an explicit one-dimensional array type on its assignment target.");
        var elementTypes = elements.Select(InferExpressionType).Distinct().ToArray();
        if (elementTypes.Length != 1)
            throw Error(array, "Typed @() expressions require one homogeneous CLR element type.");
        return elementTypes[0].MakeArrayType();
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

    private static Type? GetContextualArrayType(ArrayExpressionAst array)
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
