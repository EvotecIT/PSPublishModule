namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Applies one authored increment or decrement to a bounded member or indexed target.</summary>
        /// <remarks>The native compiler owns target evaluation, conversion, storage, and prefix/postfix results.</remarks>
        public object? MutateAccess(string target, string operation, string file, int line, int column,
            string sourceDocument, int startOffset, int endOffset)
        {
            EnsureActive();
            var mutation = GetAssignmentPlan(target, operation, file, line, column, sourceDocument, startOffset, endOffset, mutation: true);
            return mutation.Invoke(FunctionContext, null);
        }

        private NativeAstOperation CreateAccessMutation(string target, string operation, string file, int line, int column,
            string sourceDocument, int startOffset, int endOffset, out bool reusable)
        {
            var token = operation switch
            {
                "Increment" => TokenKind.PlusPlus,
                "Decrement" => TokenKind.MinusMinus,
                "PostIncrement" => TokenKind.PostfixPlusPlus,
                "PostDecrement" => TokenKind.PostfixMinusMinus,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            var document = ParseAssignmentDocument(sourceDocument, file, out var errors);
            ValidateSelectedSyntax(errors,
                token is TokenKind.PlusPlus or TokenKind.MinusMinus ?
                    (token == TokenKind.PlusPlus ? "++" : "--") + target :
                    target + (token == TokenKind.PostfixPlusPlus ? "++" : "--"), nameof(target));
            var authored = document.FindAll(node => node is UnaryExpressionAst unary && unary.TokenKind == token &&
                unary.Child.Extent.Text == target && unary.Child.Extent.StartOffset == startOffset &&
                unary.Child.Extent.EndOffset == endOffset && unary.Child.Extent.StartLineNumber == line &&
                unary.Child.Extent.StartColumnNumber == column, searchNestedScriptBlocks: true)
                .Cast<UnaryExpressionAst>().SingleOrDefault();
            if (authored is null || authored.Child is not MemberExpressionAst and not IndexExpressionAst ||
                FindNativeAccessMutationReceiver(authored.Child) is null)
                throw new ArgumentException("The native mutation does not match its qualified authored access target.", nameof(target));
            reusable = CanReuseAssignmentPlan(authored.Child);
            var selected = (UnaryExpressionAst)authored.Copy();
            var statement = new CommandExpressionAst(selected.Extent, selected, null);
            var ast = new ScriptBlockAst(selected.Extent, null,
                new StatementBlockAst(selected.Extent, new[] { statement }, null), isFilter: false);
            var native = new NativeAstCompiler(this, ast, assignmentVariables: true,
                preserveSelectedSequencePoints: selected.Child.Find(static node => node is SubExpressionAst,
                    searchNestedScriptBlocks: false) is not null);
            var result = (Expression)native.Invoke("VisitUnaryExpression", selected)!;
            native.Expressions.Add(Expression.Convert(result, typeof(object)));
            return native.Compile();
        }

        internal static VariableExpressionAst? FindNativeAccessMutationReceiver(ExpressionAst target)
            => target switch
            {
                VariableExpressionAst variable when variable.VariablePath.IsUnqualified || variable.VariablePath.IsScript ||
                    variable.VariablePath.UserPath.StartsWith("private:", StringComparison.OrdinalIgnoreCase) => variable,
                MemberExpressionAst { Static: false, Member: StringConstantExpressionAst } member
                    when member is not InvokeMemberExpressionAst &&
                         member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true
                    => FindNativeAccessMutationReceiver(member.Expression),
                IndexExpressionAst index when IsNativeAssignmentIndex(index.Index) &&
                    index.GetType().GetProperty("NullConditional")?.GetValue(index) is not true
                    => FindNativeAccessMutationReceiver(index.Target),
                _ => null
            };
    }
}
