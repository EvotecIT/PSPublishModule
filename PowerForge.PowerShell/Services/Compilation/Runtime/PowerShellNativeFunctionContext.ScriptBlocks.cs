namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly ConditionalWeakTable<PSModuleInfo, NativePredicateCache> PredicateCaches = new();

        private sealed class NativePredicateCache
        {
            internal readonly Dictionary<Tuple<string, int, int, MethodInfo?, MethodInfo?, MethodInfo?, MethodInfo?>, ScriptBlock> Bodies = new();
        }

        /// <summary>Retains a switch predicate's constant compiled body within its defining module.</summary>
        /// <remarks>Cache values contain static callbacks and native module binding, never an active invocation context.</remarks>
        public ScriptBlock CreateSwitchPredicate(string sourceDocument, int startOffset, int endOffset,
            Action<PowerShellNativeFunctionContext>? begin, Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end, Action<PowerShellNativeFunctionContext>? clean = null)
        {
            EnsureActive();
            var module = _session.Module
                ?? throw new NotSupportedException("Compiled switch predicates require a defining PowerShell module.");
            var key = Tuple.Create(sourceDocument, startOffset, endOffset, begin?.Method, process?.Method, end?.Method, clean?.Method);
            var cache = PredicateCaches.GetValue(module, _ => new NativePredicateCache());
            lock (cache.Bodies)
            {
                if (!cache.Bodies.TryGetValue(key, out var body))
                {
                    var file = _contract.DefiningFile.GetValue(FunctionContext) as string ?? string.Empty;
                    body = PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, sourceDocument, file,
                        startOffset, endOffset, begin, process, end, clean);
                    cache.Bodies.Add(key, body);
                }
                return body;
            }
        }

        /// <summary>Creates a compiled script block in the current defining module without capturing an invocation context.</summary>
        /// <remarks>Each invocation receives its own native parameter and local storage. Free variable reads use PowerShell's dynamic scope.</remarks>
        public ScriptBlock CreateScriptBlock(string sourceDocument, int startOffset, int endOffset,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
        {
            EnsureActive();
            var module = _session.Module
                ?? throw new NotSupportedException("Compiled script blocks require a defining PowerShell module.");
            var file = _contract.DefiningFile.GetValue(FunctionContext) as string ?? string.Empty;
            return PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, sourceDocument, file, startOffset, endOffset,
                begin, process, end, clean);
        }
    }
}
