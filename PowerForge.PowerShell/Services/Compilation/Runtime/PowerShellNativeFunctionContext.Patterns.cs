namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<MethodInfo> LikeOperation = new(() => FindPatternOperation("LikeOperator", typeof(TokenKind)));
        private static readonly Lazy<MethodInfo> SplitOperation = new(() => FindPatternOperation("SplitOperator", typeof(bool)));

        /// <summary>Applies native wildcard or split semantics to already evaluated operands in the active scope.</summary>
        public object? EvaluatePattern(string operation, bool ignoreCase, object? left, object? right,
            string file, int line, int column, int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            if (operation == "NativeSplit")
                return PowerShellNativeFunctionHost.Invoke(SplitOperation.Value, null, new object[] { _executionContext, extent, left!, right!, ignoreCase });
            var token = operation == "NativeLike" ? (ignoreCase ? TokenKind.Ilike : TokenKind.Clike) :
                operation == "NativeNotLike" ? (ignoreCase ? TokenKind.Inotlike : TokenKind.Cnotlike) :
                throw new ArgumentOutOfRangeException(nameof(operation));
            return PowerShellNativeFunctionHost.Invoke(LikeOperation.Value, null, new object[] { _executionContext, extent, left!, right!, token });
        }

        private static MethodInfo FindPatternOperation(string name, Type optionType)
            => typeof(PSObject).Assembly.GetType("System.Management.Automation.ParserOps", true)!
                .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { PowerShellNativeFunctionHost.NativeContract.Shared.ExecutionContext.FieldType,
                        typeof(IScriptExtent), typeof(object), typeof(object), optionType }, null)
                ?? throw new NotSupportedException("PowerShell's native pattern operation is unavailable: " + name);
    }
}
