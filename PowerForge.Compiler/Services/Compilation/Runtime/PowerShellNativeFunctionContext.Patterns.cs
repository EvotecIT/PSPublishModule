namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<MethodInfo> LikeOperation = new(() => FindPatternOperation("LikeOperator", typeof(TokenKind)));
        private static readonly Lazy<MethodInfo> MatchOperation = new(() => FindPatternOperation("MatchOperator", typeof(bool), typeof(bool)));
        private static readonly Lazy<MethodInfo> SplitOperation = new(() => FindPatternOperation("SplitOperator", typeof(bool)));
        private static readonly Lazy<MethodInfo> ReplaceOperation = new(() => FindPatternOperation("ReplaceOperator", typeof(bool)));
        private static readonly Lazy<MethodInfo?> RangeOperation = new(FindRangeOperation);
        private static readonly Lazy<MethodInfo> IntRangeOperation = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.IntOps", true)!
            .GetMethod("Range", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(int), typeof(int) }, null)
            ?? throw new NotSupportedException("PowerShell's native integer range operation is unavailable."));
        private static readonly Lazy<MethodInfo> ConvertRangeEndpoint = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.ParserOps", true)!
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(method => method.Name == "ConvertTo" && method.IsGenericMethodDefinition && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(int)));

        /// <summary>Prepares a range's left endpoint before the generated caller evaluates the right endpoint.</summary>
        public object BeginRange(object? left, string file, int line, int column,
            int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            return RangeOperation.Value is null
                ? new NativeRangeStart(ConvertLegacyRangeEndpoint(left, extent), extent, legacy: true)
                : new NativeRangeStart(left, extent, legacy: false);
        }

        /// <summary>Completes a prepared range after the generated caller evaluates the right endpoint.</summary>
        public object? EvaluateRange(object preparedLeft, object? right)
        {
            EnsureActive();
            var start = (NativeRangeStart)preparedLeft;
            if (!start.Legacy)
                return PowerShellNativeFunctionHost.Invoke(RangeOperation.Value!, null, new object[] { start.Left!, right! });
            var upper = ConvertLegacyRangeEndpoint(right, start.Extent);
            return PowerShellNativeFunctionHost.Invoke(IntRangeOperation.Value, null, new[] { start.Left!, upper! });
        }

        /// <summary>Applies native pattern semantics to already evaluated operands in the active scope.</summary>
        public object? EvaluatePattern(string operation, bool ignoreCase, object? left, object? right,
            string file, int line, int column, int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            if (operation is "NativeMatch" or "NativeNotMatch")
                return PowerShellNativeFunctionHost.Invoke(MatchOperation.Value, null, new object[] {
                    _executionContext, extent, left!, right!, operation == "NativeNotMatch", ignoreCase });
            if (operation == "NativeSplit")
                return PowerShellNativeFunctionHost.Invoke(SplitOperation.Value, null, new object[] { _executionContext, extent, left!, right!, ignoreCase });
            if (operation == "NativeReplace")
                return PowerShellNativeFunctionHost.Invoke(ReplaceOperation.Value, null, new object[] { _executionContext, extent, left!, right!, ignoreCase });
            var token = operation == "NativeLike" ? (ignoreCase ? TokenKind.Ilike : TokenKind.Clike) :
                operation == "NativeNotLike" ? (ignoreCase ? TokenKind.Inotlike : TokenKind.Cnotlike) :
                throw new ArgumentOutOfRangeException(nameof(operation));
            return PowerShellNativeFunctionHost.Invoke(LikeOperation.Value, null, new object[] { _executionContext, extent, left!, right!, token });
        }

        private static MethodInfo FindPatternOperation(string name, params Type[] optionTypes)
            => typeof(PSObject).Assembly.GetType("System.Management.Automation.ParserOps", true)!
                .GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { PowerShellNativeFunctionHost.NativeContract.Shared.ExecutionContext.FieldType,
                        typeof(IScriptExtent), typeof(object), typeof(object) }.Concat(optionTypes).ToArray(), null)
                ?? throw new NotSupportedException("PowerShell's native pattern operation is unavailable: " + name);

        private static MethodInfo? FindRangeOperation()
            => typeof(PSObject).Assembly.GetType("System.Management.Automation.ParserOps", true)!
                .GetMethod("RangeOperator", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(object), typeof(object) }, null);

        private static object ConvertLegacyRangeEndpoint(object? value, IScriptExtent extent)
        {
            try
            {
                return PowerShellNativeFunctionHost.Invoke(ConvertRangeEndpoint.Value, null, new object[] { value!, extent })!;
            }
            catch (RuntimeException error) when (error.InnerException is Exception conversionError)
            {
                // Windows PowerShell's generated range expression surfaces the conversion
                // exception owned by the inner LanguagePrimitives conversion contract.
                ExceptionDispatchInfo.Capture(conversionError).Throw();
                throw;
            }
        }

        private sealed class NativeRangeStart
        {
            internal NativeRangeStart(object? left, IScriptExtent extent, bool legacy)
            {
                Left = left;
                Extent = extent;
                Legacy = legacy;
            }

            internal object? Left { get; }
            internal IScriptExtent Extent { get; }
            internal bool Legacy { get; }
        }
    }
}
