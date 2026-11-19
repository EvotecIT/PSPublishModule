namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Dynamic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<MethodInfo> ArrayAssignmentBinder = new(() => typeof(Ast).Assembly
            .GetType("System.Management.Automation.Language.PSArrayAssignmentRHSBinder", true)!
            .GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null)
            ?? throw new NotSupportedException("PowerShell's multiple assignment binder is unavailable."));

        /// <summary>Applies an authored storage or receiver assignment in the native invocation.</summary>
        /// <remarks>The value producer runs at the native assignment's evaluation point, after any compound target read.</remarks>
        public object? AssignTarget(string target, string operation, Func<object?> value,
            string file, int line, int column, string? sourceDocument = null, int startOffset = -1, int endOffset = -1,
            Action<IScriptExtent>? completedPosition = null)
        {
            EnsureActive();
            if (value is null) throw new ArgumentNullException(nameof(value));
            var assignment = GetAssignmentPlan(target, operation, file, line, column, sourceDocument, startOffset, endOffset, mutation: false);
            return assignment.Invoke(FunctionContext, value, rightHandSideFirst: operation == "Assign",
                completedPosition: completedPosition);
        }

        private NativeAstOperation CreateAssignmentTarget(string target, string operation, string file, int line, int column,
            string? sourceDocument, int startOffset, int endOffset, out bool reusable)
        {
            if (line < 1 || column < 1) throw new ArgumentOutOfRangeException(nameof(line));
            var token = operation switch
            {
                "Assign" => TokenKind.Equals,
                "Add" => TokenKind.PlusEquals,
                "Subtract" => TokenKind.MinusEquals,
                "Multiply" => TokenKind.MultiplyEquals,
                "Divide" => TokenKind.DivideEquals,
                "Remainder" => TokenKind.RemainderEquals,
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            var parseText = sourceDocument ?? new string('\n', line - 1) + new string(' ', column - 1) + target + " = $null";
            var document = ParseAssignmentDocument(parseText, file, out var errors);
            ValidateSelectedSyntax(errors, target + " = $null", nameof(target));
            var targetAst = document.FindAll(node => node is AssignmentStatementAst or ForEachStatementAst, searchNestedScriptBlocks: true)
                .Select(node => node is AssignmentStatementAst assignment ? assignment.Left :
                    token == TokenKind.Equals ? ((ForEachStatementAst)node).Variable : null)
                .FirstOrDefault(node => node is not null &&
                    node.Extent.Text == target && node.Extent.StartLineNumber == line &&
                    node.Extent.StartColumnNumber == column && (sourceDocument is null ||
                    node.Extent.StartOffset == startOffset && node.Extent.EndOffset == endOffset));
            if (targetAst is null)
                throw new ArgumentException("The native assignment does not match its authored target.", nameof(target));
            ExpressionAst variable = targetAst;
            while (variable is AttributedExpressionAst attributed) variable = attributed.Child;
            if (!IsNativeAssignmentAccess(variable) &&
                !(token == TokenKind.Equals && IsNativeVariableTuple(variable)))
                throw new ArgumentException("The native assignment requires a qualified variable, member, or index target.", nameof(target));
            reusable = CanReuseAssignmentPlan(targetAst);

            // Only the target reaches native compilation. The authored RHS remains compiled IR;
            // a harmless placeholder supplies the analysis container and is never emitted.
            var extent = targetAst.Extent;
            var placeholder = new CommandExpressionAst(extent, new ConstantExpressionAst(extent, null), null);
            var detached = new AssignmentStatementAst(extent, (ExpressionAst)targetAst.Copy(), token, placeholder, extent);
            var ast = new ScriptBlockAst(extent, null, new StatementBlockAst(extent, new[] { detached }, null), isFilter: false);
            var native = new NativeAstCompiler(this, ast, assignmentVariables: true,
                preserveSelectedSequencePoints: targetAst.Find(static node => node is SubExpressionAst,
                    searchNestedScriptBlocks: false) is not null);
            Expression right = Expression.Invoke(native.RightHandSide);
            if (variable is ArrayLiteralAst tuple)
            {
                var binder = (DynamicMetaObjectBinder)PowerShellNativeFunctionHost.Invoke(
                    ArrayAssignmentBinder.Value, null, new object[] { tuple.Elements.Count })!;
                right = Expression.Dynamic(binder, typeof(IList), right);
            }
            var result = (Expression)native.Invoke("ReduceAssignment", detached.Left, token, right)!;
            native.Expressions.Add(Expression.Convert(result, typeof(object)));
            return native.Compile();
        }

        private static bool IsNativeAssignmentAccess(ExpressionAst expression)
            => FindNativeStaticAccessReceiver(expression) is not null || expression switch
            {
                VariableExpressionAst => true,
                MemberExpressionAst { Static: false } member => IsNativeAssignmentAccess(member.Expression),
                IndexExpressionAst index when IsNativeAssignmentIndex(index.Index) => IsNativeAssignmentAccess(index.Target),
                _ => false
            };

        internal static bool IsNativeVariableTuple(ExpressionAst expression)
            => expression is ArrayLiteralAst { Elements.Count: > 1 } tuple &&
               tuple.Elements.All(static element => element is VariableExpressionAst variable &&
                   (variable.VariablePath.IsUnqualified || variable.VariablePath.IsLocal));

        /// <summary>Finds a literal static CLR root below a bounded member/index assignment target.</summary>
        internal static TypeExpressionAst? FindNativeStaticAccessReceiver(ExpressionAst target)
            => target switch
            {
                MemberExpressionAst { Member: StringConstantExpressionAst } member
                    when member is not InvokeMemberExpressionAst &&
                         member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true
                    => member.Static ? member.Expression as TypeExpressionAst : FindNativeStaticAccessReceiver(member.Expression),
                IndexExpressionAst index when IsNativeAssignmentIndex(index.Index) &&
                    index.GetType().GetProperty("NullConditional")?.GetValue(index) is not true
                    => FindNativeStaticAccessReceiver(index.Target),
                _ => null
            };

        /// <summary>Shares bounded target admission with the semantic binder; interpolation cannot introduce another body.</summary>
        internal static bool IsNativeAssignmentIndex(ExpressionAst index)
            => index is ConstantExpressionAst or StringConstantExpressionAst or VariableExpressionAst ||
               index is BinaryExpressionAst { Operator: TokenKind.Plus or TokenKind.Minus or TokenKind.Multiply or
                   TokenKind.Divide or TokenKind.Rem } binary &&
               IsNativeAssignmentIndex(binary.Left) && IsNativeAssignmentIndex(binary.Right) ||
               index is UnaryExpressionAst { TokenKind: TokenKind.Plus or TokenKind.Minus } unary &&
               IsNativeAssignmentIndex(unary.Child) ||
               index is ParenExpressionAst { Pipeline: PipelineAst parenthesized } && IsNativeAssignmentIndexPipeline(parenthesized, allowCommand: true) ||
               index is SubExpressionAst { SubExpression.Statements.Count: 1 } subexpression &&
               (subexpression.SubExpression.Traps is null || subexpression.SubExpression.Traps.Count == 0) &&
               subexpression.SubExpression.Statements[0] is PipelineAst subPipeline && IsNativeAssignmentIndexPipeline(subPipeline) ||
               index is ExpandableStringExpressionAst expanded &&
               expanded.NestedExpressions.All(IsNativeAssignmentInterpolation) ||
               IsNativeAssignmentKeyMember(index);

        private static bool IsNativeAssignmentKeyMember(ExpressionAst expression)
            => expression is MemberExpressionAst { Static: false, Member: StringConstantExpressionAst } member &&
               member is not InvokeMemberExpressionAst &&
               member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true &&
               (member.Expression is VariableExpressionAst { VariablePath.IsUnqualified: true } ||
                IsNativeAssignmentKeyMember(member.Expression));

        private static bool IsNativeAssignmentIndexPipeline(PipelineAst pipeline, bool allowCommand = false)
            => pipeline.PipelineElements.Count == 1 && !IsBackgroundAssignmentPipeline(pipeline) &&
               (pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } expression &&
                IsNativeAssignmentIndex(expression.Expression) ||
                allowCommand && pipeline.PipelineElements[0] is CommandAst
                    { InvocationOperator: TokenKind.Unknown, Redirections.Count: 0 } command &&
                command.GetCommandName() is not null && command.CommandElements.Skip(1).All(element =>
                    element is ExpressionAst argument && IsNativeAssignmentIndex(argument) ||
                    element is CommandParameterAst parameter &&
                    (parameter.Argument is null || IsNativeAssignmentIndex(parameter.Argument))));

        private static bool IsNativeAssignmentInterpolation(ExpressionAst expression)
            => expression is VariableExpressionAst { VariablePath.IsUnqualified: true } ||
               expression is IndexExpressionAst index &&
               index.GetType().GetProperty("NullConditional")?.GetValue(index) is not true &&
               IsNativeAssignmentIndex(index.Index) && FindNativeAccessMutationReceiver(index.Target) is not null ||
               expression is MemberExpressionAst { Static: false, Expression: VariableExpressionAst receiver, Member: StringConstantExpressionAst } member &&
               member is not InvokeMemberExpressionAst &&
               member.GetType().GetProperty("NullConditional")?.GetValue(member) is not true &&
               receiver.VariablePath.IsUnqualified ||
               expression is SubExpressionAst { SubExpression.Statements.Count: 1 } sub &&
               (sub.SubExpression.Traps is null || sub.SubExpression.Traps.Count == 0) &&
               sub.SubExpression.Statements[0] is PipelineAst { PipelineElements.Count: 1 } pipeline &&
               !IsBackgroundAssignmentPipeline(pipeline) &&
               pipeline.PipelineElements[0] is CommandExpressionAst { Redirections.Count: 0 } command &&
               IsNativeAssignmentInterpolation(command.Expression);

        private static bool IsBackgroundAssignmentPipeline(PipelineAst pipeline)
            => pipeline.GetType().GetProperty("Background")?.GetValue(pipeline) is true;
    }
}
