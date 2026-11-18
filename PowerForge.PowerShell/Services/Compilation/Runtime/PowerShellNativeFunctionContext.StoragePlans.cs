namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<FieldInfo> LocalNames = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.MutableTuple", true)!
            .GetField("_nameToIndexMap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!);

        private static Dictionary<string, int> GetLocalNameMap(object tuple)
            => (Dictionary<string, int>)LocalNames.Value.GetValue(tuple)!;

        private NativeStoragePlans GetStoragePlans()
        {
            var tuple = _contract.LocalsTuple.GetValue(FunctionContext)!;
            var names = GetLocalNameMap(tuple);
            var owner = GetNativeDefinitionPlans();
            // Two layouts can use the same tuple type and put a name at different
            // indexes. PowerShell shares the name map across tuples of one layout.
            var layouts = owner.Storage.GetValue(names,
                static _ => new ConcurrentDictionary<(Type, bool, PSLanguageMode), NativeStoragePlans>());
            return layouts.GetOrAdd((tuple.GetType(), _optimized, _session.LanguageMode), static _ => new NativeStoragePlans());
        }

        private NativeAstOperation GetAssignmentPlan(string target, string operation, string file, int line, int column,
            string? source, int start, int end, bool mutation)
        {
            var site = (file, line, column, start, end, operation, target, mutation);
            var key = (GetCommandRegionSourceId(source), site);
            if (_assignments.TryGetValue(key, out var plan)) return plan;

            var plans = source is null ? null : GetStoragePlans().Assignments.GetValue(source, static _ => new());
            if (plans is null || !plans.TryGetValue(site, out plan))
            {
                bool reusable;
                plan = mutation
                    ? CreateAccessMutation(target, operation, file, line, column, source!, start, end, out reusable)
                    : CreateAssignmentTarget(target, operation, file, line, column, source, start, end, out reusable);
                if (reusable && plans is not null) plans.TryAdd(site, plan);
            }

            _assignments.Add(key, plan);
            return plan;
        }

        private static bool CanReuseAssignmentPlan(ExpressionAst target)
            // Native PowerShell can bind type names while compiling. Keep those
            // targets and embedded commands on the invocation's compilation path.
            => target.Find(static node => node is TypeExpressionAst or TypeConstraintAst or AttributeAst or
                CommandAst or ScriptBlockExpressionAst, searchNestedScriptBlocks: true) is null;

        private NativeVariableStorage GetVariableStorage(string name, int? automaticIndex = null)
        {
            var plans = GetStoragePlans().Variables;
            var key = (name, automaticIndex);
            if (plans.TryGetValue(key, out var plan)) return plan;
            return plans.GetOrAdd(key, new NativeVariableStorage(this, name, automaticIndex));
        }

        private sealed partial class NativeDefinitionPlans
        {
            internal ConditionalWeakTable<Dictionary<string, int>, ConcurrentDictionary<(Type, bool, PSLanguageMode), NativeStoragePlans>> Storage { get; } = new();
        }

        private sealed class NativeStoragePlans
        {
            internal ConditionalWeakTable<string, ConcurrentDictionary<(string, int, int, int, int, string, string, bool), NativeAstOperation>> Assignments { get; } = new();
            internal ConcurrentDictionary<(string, int?), NativeVariableStorage> Variables { get; } = new();
        }

        private readonly Dictionary<(long, (string, int, int, int, int, string, string, bool)), NativeAstOperation> _assignments = new();
    }
}
