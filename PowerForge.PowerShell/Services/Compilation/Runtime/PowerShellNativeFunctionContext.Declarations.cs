namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation.Language;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private readonly Dictionary<string, NativeAstOperation> _assignments = new(StringComparer.Ordinal);

        /// <summary>Applies an authored storage or receiver assignment in the native invocation.</summary>
        /// <remarks>The value producer runs at the native assignment's evaluation point, after any compound target read.</remarks>
        public object? AssignTarget(string target, string operation, Func<object?> value,
            string file, int line, int column, string? sourceDocument = null, int startOffset = -1, int endOffset = -1)
        {
            EnsureActive();
            if (value is null) throw new ArgumentNullException(nameof(value));
            var key = file + "\0" + line + "\0" + column + "\0" + startOffset + "\0" + endOffset +
                "\0" + operation + "\0" + target + "\0" + sourceDocument;
            if (!_assignments.TryGetValue(key, out var assignment))
                _assignments.Add(key, assignment = CreateAssignmentTarget(target, operation, file, line, column,
                    sourceDocument, startOffset, endOffset));
            return assignment.Invoke(FunctionContext, value);
        }

        private NativeAstOperation CreateAssignmentTarget(string target, string operation, string file, int line, int column,
            string? sourceDocument, int startOffset, int endOffset)
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
            var parseArguments = new object[]
            {
                sourceDocument ?? new string('\n', line - 1) + new string(' ', column - 1) + target + " = $null",
                file, null!, null!
            };
            var document = (ScriptBlockAst)PowerShellNativeFunctionHost.Invoke(_contract.ParseInputWithFile, null, parseArguments)!;
            var errors = (ParseError[])parseArguments[3];
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
            if (!IsNativeAssignmentAccess(variable))
                throw new ArgumentException("The native assignment requires a qualified variable, member, or index target.", nameof(target));

            // Only the target reaches native compilation. The authored RHS remains compiled IR;
            // a harmless placeholder supplies the analysis container and is never emitted.
            var extent = targetAst.Extent;
            var placeholder = new CommandExpressionAst(extent, new ConstantExpressionAst(extent, null), null);
            var detached = new AssignmentStatementAst(extent, (ExpressionAst)targetAst.Copy(), token, placeholder, extent);
            var ast = new ScriptBlockAst(extent, null, new StatementBlockAst(extent, new[] { detached }, null), isFilter: false);
            var native = new NativeAstCompiler(this, ast, assignmentVariables: true);
            var result = (Expression)native.Invoke("ReduceAssignment", detached.Left, token, Expression.Invoke(native.RightHandSide))!;
            native.Expressions.Add(Expression.Convert(result, typeof(object)));
            return native.Compile();
        }

        private static bool IsNativeAssignmentAccess(ExpressionAst expression)
            => expression switch
            {
                VariableExpressionAst => true,
                MemberExpressionAst { Static: false, Member: StringConstantExpressionAst } member => IsNativeAssignmentAccess(member.Expression),
                IndexExpressionAst { Index: ConstantExpressionAst or StringConstantExpressionAst or VariableExpressionAst } index => IsNativeAssignmentAccess(index.Target),
                _ => false
            };
    }
}
