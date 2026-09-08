namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Dynamic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly ConcurrentDictionary<InvocationSiteKey, Func<object?, object?[], object?>> InvocationSites = new();

        /// <summary>Invokes a method or constructor on the already evaluated target using native overload resolution.</summary>
        public object? InvokeMember(object? receiver, string name, bool isStatic, object?[] arguments,
            Type? targetConstraint, Type?[] argumentConstraints)
        {
            EnsureActive();
            return InvocationSites.GetOrAdd(new InvocationSiteKey(name, isStatic, targetConstraint, argumentConstraints),
                CreateInvocationSite)(receiver, arguments);
        }

        private static Func<object?, object?[], object?> CreateInvocationSite(InvocationSiteKey key)
        {
            var assembly = typeof(PSObject).Assembly;
            var constraintsType = assembly.GetType("System.Management.Automation.PSMethodInvocationConstraints", true)!;
            var constructor = constraintsType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Type), typeof(Type[]) }, null)
                ?? throw new NotSupportedException("PowerShell's native invocation constraints are unavailable.");
            var constraints = key.TargetConstraint is null && key.ArgumentConstraints.Length == 0 ? null :
                constructor.Invoke(new object?[] { key.TargetConstraint, key.ArgumentConstraints });
            var create = key.IsStatic && key.Name.Equals("new", StringComparison.OrdinalIgnoreCase);
            var type = assembly.GetType("System.Management.Automation.Language." + (create ? "PSCreateInstanceBinder" : "PSInvokeMemberBinder"), true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                create ? new[] { typeof(CallInfo), constraintsType, typeof(bool) } :
                    new[] { typeof(string), typeof(CallInfo), typeof(bool), typeof(bool), constraintsType, typeof(Type) }, null)
                ?? throw new NotSupportedException("PowerShell's native invocation binder is unavailable.");
            var info = new CallInfo(key.ArgumentConstraints.Length);
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null,
                create ? new object[] { info, constraints!, true } : new object[] { key.Name, info, key.IsStatic, false, constraints!, null! })!;
            var receiver = Expression.Parameter(typeof(object), "receiver");
            var arguments = Expression.Parameter(typeof(object[]), "arguments");
            var operands = new Expression[] { receiver }.Concat(Enumerable.Range(0, key.ArgumentConstraints.Length)
                .Select(index => (Expression)Expression.ArrayIndex(arguments, Expression.Constant(index))));
            return Expression.Lambda<Func<object?, object?[], object?>>(
                Expression.Dynamic(binder, typeof(object), operands), receiver, arguments).Compile();
        }

        // The key captures only authored metadata; it never captures a target, argument value, or session.
        private sealed class InvocationSiteKey : IEquatable<InvocationSiteKey>
        {
            internal InvocationSiteKey(string name, bool isStatic, Type? targetConstraint, Type?[] argumentConstraints)
            {
                Name = name;
                IsStatic = isStatic;
                TargetConstraint = targetConstraint;
                ArgumentConstraints = (Type?[])argumentConstraints.Clone();
            }

            internal string Name { get; }
            internal bool IsStatic { get; }
            internal Type? TargetConstraint { get; }
            internal Type?[] ArgumentConstraints { get; }

            public bool Equals(InvocationSiteKey? other) => other is not null && Name == other.Name && IsStatic == other.IsStatic &&
                TargetConstraint == other.TargetConstraint && ArgumentConstraints.SequenceEqual(other.ArgumentConstraints);
            public override bool Equals(object? other) => Equals(other as InvocationSiteKey);
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = (StringComparer.Ordinal.GetHashCode(Name) * 397) ^ IsStatic.GetHashCode();
                    hash = (hash * 397) ^ (TargetConstraint?.GetHashCode() ?? 0);
                    foreach (var type in ArgumentConstraints) hash = (hash * 397) ^ (type?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }
    }
}
