namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        // Native rules own adapter, type-table and language-mode invalidation. No invocation is cached.
        private static readonly ConcurrentDictionary<string, Func<object?, object?>> MemberReadSites = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<bool, Func<object?, object?, object?>> DynamicMemberReadSites = new();

        /// <summary>Reads one member using the active PowerShell session's native semantics.</summary>
        public object? ReadMember(object? receiver, string name)
        {
            EnsureActive();
            return MemberReadSites.GetOrAdd(name, CreateMemberReadSite)(receiver);
        }

        /// <summary>Reads one computed member using the active PowerShell session's native semantics.</summary>
        public object? ReadDynamicMember(object? receiver, object? name, bool isStatic)
        {
            EnsureActive();
            return DynamicMemberReadSites.GetOrAdd(isStatic, CreateDynamicMemberReadSite)(receiver, name);
        }

        private static Func<object?, object?> CreateMemberReadSite(string name)
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSGetMemberBinder", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(string), typeof(Type), typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native member-read binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, new object[] { name, null!, false })!;
            var receiver = Expression.Parameter(typeof(object), "receiver");
            return Expression.Lambda<Func<object?, object?>>(
                Expression.Dynamic(binder, typeof(object), receiver), receiver).Compile();
        }

        private static Func<object?, object?, object?> CreateDynamicMemberReadSite(bool isStatic)
        {
            var assembly = typeof(PSObject).Assembly;
            var type = assembly.GetType("System.Management.Automation.Language.PSGetDynamicMemberBinder", true)!;
            var classScope = assembly.GetType("System.Management.Automation.Language.TypeDefinitionAst", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { classScope, typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native computed-member binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, new object[] { null!, isStatic })!;
            var receiver = Expression.Parameter(typeof(object), "receiver");
            var name = Expression.Parameter(typeof(object), "name");
            return Expression.Lambda<Func<object?, object?, object?>>(
                Expression.Dynamic(binder, typeof(object), receiver, name), receiver, name).Compile();
        }
    }
}
