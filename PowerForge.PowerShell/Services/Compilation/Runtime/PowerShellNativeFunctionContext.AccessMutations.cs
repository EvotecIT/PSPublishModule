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
            var key = "access-mutation\0" + file + "\0" + startOffset + "\0" + endOffset + "\0" +
                operation + "\0" + target + "\0" + sourceDocument;
            if (!_assignments.TryGetValue(key, out var mutation))
                _assignments.Add(key, mutation = CreateAccessMutation(target, operation, file, line, column,
                    sourceDocument, startOffset, endOffset));
            return mutation.Invoke(FunctionContext, null);
        }

        private NativeAstOperation CreateAccessMutation(string target, string operation, string file, int line, int column,
            string sourceDocument, int startOffset, int endOffset)
        {
            var token = operation switch
            {
                "Increment" => TokenKind.PlusPlus,
                "Decrement" => TokenKind.MinusMinus,
                "PostIncrement" => TokenKind.PostfixPlusPlus,
                "PostDecrement" => TokenKind.PostfixMinusMinus,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            var arguments = new object[] { sourceDocument, file, null!, null! };
            var document = (ScriptBlockAst)PowerShellNativeFunctionHost.Invoke(_contract.ParseInputWithFile, null, arguments)!;
            ValidateSelectedSyntax((ParseError[])arguments[3],
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
            var selected = (UnaryExpressionAst)authored.Copy();
            var statement = new CommandExpressionAst(selected.Extent, selected, null);
            var ast = new ScriptBlockAst(selected.Extent, null,
                new StatementBlockAst(selected.Extent, new[] { statement }, null), isFilter: false);
            var native = new NativeAstCompiler(this, ast, assignmentVariables: true);
            var result = (Expression)native.Invoke("VisitUnaryExpression", selected)!;
            native.Expressions.Add(Expression.Convert(result, typeof(object)));
            return native.Compile();
        }

        internal static bool IsBoundedNativeAccessTarget(ExpressionAst target)
            => target is MemberExpressionAst { Static: false, Expression: VariableExpressionAst memberReceiver,
                Member: StringConstantExpressionAst } member && member is not InvokeMemberExpressionAst &&
                memberReceiver.VariablePath.IsUnqualified &&
                member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true ||
               target is IndexExpressionAst { Target: VariableExpressionAst indexReceiver } index &&
                indexReceiver.VariablePath.IsUnqualified && IsNativeAssignmentIndex(index.Index) &&
                index.GetType().GetProperty("NullConditional")?.GetValue(index) is not true;

        internal static VariableExpressionAst? FindNativeAccessMutationReceiver(ExpressionAst target)
            => target switch
            {
                VariableExpressionAst variable when variable.VariablePath.IsUnqualified => variable,
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
