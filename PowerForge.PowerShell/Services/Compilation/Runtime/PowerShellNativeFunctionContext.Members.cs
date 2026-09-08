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

        /// <summary>Reads one member using the active PowerShell session's native semantics.</summary>
        public object? ReadMember(object? receiver, string name)
        {
            EnsureActive();
            return MemberReadSites.GetOrAdd(name, CreateMemberReadSite)(receiver);
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
    }
}
