namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly ConcurrentDictionary<Tuple<int, Type?, Type?>, Func<object?, object?[], object?>> IndexReadSites = new();

        /// <summary>Indexes an evaluated target and arguments using native slicing and overload rules.</summary>
        public object? ReadIndex(object? receiver, object?[] arguments, Type? targetConstraint, Type? indexConstraint)
        {
            EnsureActive();
            return IndexReadSites.GetOrAdd(Tuple.Create(arguments.Length, targetConstraint, indexConstraint), CreateIndexReadSite)(receiver, arguments);
        }

        private static Func<object?, object?[], object?> CreateIndexReadSite(Tuple<int, Type?, Type?> key)
        {
            var assembly = typeof(PSObject).Assembly;
            var constraintsType = assembly.GetType("System.Management.Automation.PSMethodInvocationConstraints", true)!;
            object? constraints = null;
            if (key.Item2 is not null || key.Item3 is not null)
            {
                var constructor = constraintsType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Type), typeof(Type[]) }, null)
                    ?? throw new NotSupportedException("PowerShell's native indexing constraints are unavailable.");
                constraints = constructor.Invoke(new object?[] { key.Item2, new Type?[] { key.Item3 } });
            }
            var type = assembly.GetType("System.Management.Automation.Language.PSGetIndexBinder", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(int), constraintsType, typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native index-read binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, new object[] { key.Item1, constraints!, true })!;
            var receiver = Expression.Parameter(typeof(object), "receiver");
            var arguments = Expression.Parameter(typeof(object[]), "arguments");
            var operands = new Expression[] { receiver }.Concat(Enumerable.Range(0, key.Item1)
                .Select(index => (Expression)Expression.ArrayIndex(arguments, Expression.Constant(index))));
            return Expression.Lambda<Func<object?, object?[], object?>>(
                Expression.Dynamic(binder, typeof(object), operands), receiver, arguments).Compile();
        }
    }
}
