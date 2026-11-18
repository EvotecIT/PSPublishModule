namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly ConditionalWeakTable<object, ConditionalWeakTable<ScriptBlock, NativeDefinitionPlans>> NativeDefinitionOwners = new();
        private static readonly ConditionalWeakTable<string, CommandRegionSourceIdentity> CommandRegionSources = new();
        private static long _nextCommandRegionSourceId;
        private NativeDefinitionPlans? _definitionPlans;

        private NativeDefinitionPlans GetNativeDefinitionPlans()
        {
            if (_definitionPlans is not null) return _definitionPlans;
            var script = (ScriptBlock)FunctionContext.GetType().GetField("_scriptBlock",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(FunctionContext)!;
            var definitions = NativeDefinitionOwners.GetValue(_executionContext,
                static _ => new ConditionalWeakTable<ScriptBlock, NativeDefinitionPlans>());
            return _definitionPlans = definitions.GetValue(script, static _ => new NativeDefinitionPlans());
        }

        private ConcurrentDictionary<string, NativeAstOperation> GetCommandRegionPlans()
        {
            var owner = GetNativeDefinitionPlans();
            var tupleType = _contract.LocalsTuple.GetValue(FunctionContext)!.GetType();
            // Native compilation uses this function's binding metadata and local
            // tuple layout. Invocation values and output remain delegate arguments.
            var shape = Tuple.Create(tupleType, _session.LanguageMode);
            return owner.Plans.GetOrAdd(shape,
                static _ => new ConcurrentDictionary<string, NativeAstOperation>(StringComparer.Ordinal));
        }

        private static long GetCommandRegionSourceId(string? source)
            => source is null ? 0 : CommandRegionSources.GetValue(source,
                static _ => new CommandRegionSourceIdentity()).Value;

        private sealed class CommandRegionSourceIdentity
        {
            // Identity keys avoid copying the entire source document into every
            // retained operation key. Transient source strings remain weak keys.
            internal long Value { get; } = Interlocked.Increment(ref _nextCommandRegionSourceId);
        }

        private sealed partial class NativeDefinitionPlans
        {
            internal ConcurrentDictionary<Tuple<Type, PSLanguageMode>, ConcurrentDictionary<string, NativeAstOperation>> Plans { get; } = new();
        }
    }
}
