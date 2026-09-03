namespace PowerForge;

internal sealed partial class PowerShellTypedLowerer
{
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
            bool requiresPowerShellModuleStateWrite)
        {
            Function = function;
            RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
            RequiresPowerShellStreams = requiresPowerShellStreams;
            RequiresProviderCancellation = requiresProviderCancellation;
            RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
            RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
            RequiresPowerShellModuleStateRead = requiresPowerShellModuleStateRead;
            RequiresPowerShellModuleStateWrite = requiresPowerShellModuleStateWrite;
        }

        internal PowerShellBoundFunction Function { get; }
        internal bool RequiresPowerShellBoundParameters { get; }
        internal bool RequiresPowerShellStreams { get; }
        internal bool RequiresProviderCancellation { get; }
        internal bool RequiresPowerShellCommandRegions { get; }
        internal bool RequiresPowerShellRuntimeState { get; }
        internal bool RequiresPowerShellModuleStateRead { get; }
        internal bool RequiresPowerShellModuleStateWrite { get; }
        internal bool RequiresPowerShellModuleState => RequiresPowerShellModuleStateRead || RequiresPowerShellModuleStateWrite;
    }
}
