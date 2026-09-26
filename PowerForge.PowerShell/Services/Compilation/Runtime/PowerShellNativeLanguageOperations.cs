namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    /// <summary>Shared host binder operations for typed commands and native function contexts.</summary>
    internal static class PowerShellNativeLanguageOperations
    {
        private static readonly ConcurrentDictionary<bool, Func<object, object?, object?, bool>> MembershipOperations = new();
        private static readonly Lazy<Func<object?, object?, bool>> TypeTest = new(() => CreateTypeTest());

        internal static bool IsInstance(object? value, object? type) => TypeTest.Value(value, type);

        private static Func<object?, object?, bool> CreateTypeTest()
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.TypeOps", true)!;
            var method = type.GetMethod("IsInstance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(object), typeof(object) }, null)
                ?? throw new NotSupportedException("PowerShell's type-test operation is unavailable.");
            var value = Expression.Parameter(typeof(object), "value");
            var target = Expression.Parameter(typeof(object), "target");
            return Expression.Lambda<Func<object?, object?, bool>>(Expression.Call(method, value, target), value, target).Compile();
        }

        internal static object? NormalizeCommandArgument(object? value)
            => ReferenceEquals(value, System.Management.Automation.Internal.AutomationNull.Value) ? null : value;

        internal static bool EvaluateMembership(object context, bool ignoreCase, bool negate, object? collection, object? candidate)
        {
            var found = MembershipOperations.GetOrAdd(ignoreCase, CreateMembershipOperation)(context, collection, candidate);
            return negate ? !found : found;
        }

        private static Func<object, object?, object?, bool> CreateMembershipOperation(bool ignoreCase)
        {
            var enumerator = CallSite<Func<CallSite, object?, IEnumerator?>>.Create(GetSingletonBinder("PSEnumerableBinder"));
            var comparison = CallSite<Func<CallSite, object?, object?, object?>>.Create(GetBinaryBinder(ExpressionType.Equal, ignoreCase, true));
            var contextType = typeof(PSObject).Assembly.GetType("System.Management.Automation.ExecutionContext", true)!;
            var operation = typeof(PSObject).Assembly.GetType("System.Management.Automation.ParserOps", true)!
                .GetMethod("ContainsOperatorCompiled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { contextType, enumerator.GetType(), comparison.GetType(), typeof(object), typeof(object) }, null)
                ?? throw new NotSupportedException("PowerShell's native membership operation is unavailable.");
            var context = Expression.Parameter(typeof(object), "context");
            var collection = Expression.Parameter(typeof(object), "collection");
            var candidate = Expression.Parameter(typeof(object), "candidate");
            return Expression.Lambda<Func<object, object?, object?, bool>>(
                Expression.Call(operation, Expression.Convert(context, contextType), Expression.Constant(enumerator),
                    Expression.Constant(comparison), collection, candidate), context, collection, candidate).Compile();
        }

        internal static CallSiteBinder GetBinaryBinder(ExpressionType operation, bool ignoreCase, bool scalarCompare)
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSBinaryOperationBinder", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { typeof(ExpressionType), typeof(bool), typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native binary-operation binder is unavailable.");
            return Expression.Lambda<Func<CallSiteBinder>>(Expression.Convert(Expression.Call(get,
                Expression.Constant(operation), Expression.Constant(ignoreCase), Expression.Constant(scalarCompare)), typeof(CallSiteBinder))).Compile()();
        }

        internal static CallSiteBinder GetSingletonBinder(string name)
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language." + name, true)!;
            var get = type.GetMethod("Get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, Type.EmptyTypes, null) ?? throw new NotSupportedException("PowerShell's " + name + " operation is unavailable.");
            return Expression.Lambda<Func<CallSiteBinder>>(
                Expression.Convert(Expression.Call(get), typeof(CallSiteBinder))).Compile()();
        }
    }
}
