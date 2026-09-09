namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly ConcurrentDictionary<bool, Func<object, object?, object?, bool>> MembershipOperations = new();

        /// <summary>Compares an already evaluated collection and candidate using the active host's membership contract.</summary>
        public bool EvaluateMembership(bool ignoreCase, bool negate, object? collection, object? candidate)
        {
            EnsureActive();
            var found = MembershipOperations.GetOrAdd(ignoreCase, CreateMembershipOperation)(_executionContext, collection, candidate);
            return negate ? !found : found;
        }

        private static Func<object, object?, object?, bool> CreateMembershipOperation(bool ignoreCase)
        {
            var enumerator = CallSite<Func<CallSite, object?, IEnumerator?>>.Create(NativeEnumerationContract.Shared.Value.Binder);
            var comparison = CallSite<Func<CallSite, object?, object?, object?>>.Create(GetBinaryBinder(ExpressionType.Equal, ignoreCase, true));
            var contextType = PowerShellNativeFunctionHost.NativeContract.Shared.ExecutionContext.FieldType;
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
    }
}
