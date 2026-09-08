namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
    private static bool RequiresHostExceptionHandling(PowerShellBoundTryStatement statement,
        IReadOnlyDictionary<string, LoweringFunctionContext> functions)
    {
        if (PowerShellLoopInterruptContract.RequiresContext(statement.Capabilities)) return true;
        var blocks = new[] { statement.Body }.Concat(statement.Catches.Select(static clause => clause.Body));
        if (statement.FinallyBlock is not null) blocks = blocks.Append(statement.FinallyBlock);
        return blocks.SelectMany(PowerShellSemanticAnalyzer.EnumerateStatements)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .OfType<PowerShellBoundInvocationExpression>()
            .Any(call => functions.TryGetValue(call.Target.StableKey, out var target) && target.RequiresPowerShellStatementErrors);
    }

    private sealed class LoweredNameAllocator
    {
        private readonly HashSet<string> _used;
        private int _index;

        internal LoweredNameAllocator(IEnumerable<string> authoredNames)
        {
            _used = authoredNames.Select(PowerShellClrSymbolMapper.MapIdentifier).ToHashSet(StringComparer.Ordinal);
        }

        internal string Allocate(string prefix)
        {
            string candidate;
            do { candidate = $"__{prefix}_{_index++}"; } while (!_used.Add(candidate));
            return candidate;
        }
    }

    private sealed class LoweringFunctionContext
    {
        internal LoweringFunctionContext(
            PowerShellBoundFunction function,
            bool requiresPowerShellBoundParameters,
            bool requiresPowerShellStreams,
            bool requiresProviderCancellation,
            bool requiresPowerShellCommandRegions,
            bool requiresPowerShellRuntimeState,
            bool requiresPowerShellModuleStateRead,
            bool requiresPowerShellModuleStateWrite,
            bool requiresPowerShellStatementErrors = false)
        {
            Function = function;
            RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
            RequiresPowerShellStreams = requiresPowerShellStreams;
            RequiresProviderCancellation = requiresProviderCancellation;
            RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
            RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
            RequiresPowerShellModuleStateRead = requiresPowerShellModuleStateRead;
            RequiresPowerShellModuleStateWrite = requiresPowerShellModuleStateWrite;
            RequiresPowerShellStatementErrors = requiresPowerShellStatementErrors;
        }

        internal PowerShellBoundFunction Function { get; }
        internal bool RequiresPowerShellBoundParameters { get; }
        internal bool RequiresPowerShellStreams { get; }
        internal bool RequiresProviderCancellation { get; }
        internal bool RequiresPowerShellCommandRegions { get; }
        internal bool RequiresPowerShellRuntimeState { get; }
        internal bool RequiresPowerShellModuleStateRead { get; }
        internal bool RequiresPowerShellModuleStateWrite { get; }
        internal bool RequiresPowerShellStatementErrors { get; }
        internal bool RequiresPowerShellModuleState => RequiresPowerShellModuleStateRead || RequiresPowerShellModuleStateWrite;
    }
}
