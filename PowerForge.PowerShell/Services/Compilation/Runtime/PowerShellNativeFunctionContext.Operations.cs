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
        // Sites cache language rules, never invocation values or a session. The native binder
        // owns type-table and language-mode invalidation across the sessions using each site.
        private static readonly ConcurrentDictionary<Tuple<ExpressionType, bool>, Func<object?, object?, object?>> BinarySites = new();

        /// <summary>Evaluates one language operation while the native invocation owns scope and error handling.</summary>
        public object? EvaluateBinary(ExpressionType operation, bool ignoreCase, object? left, object? right)
        {
            EnsureActive();
            switch (operation)
            {
                case ExpressionType.Add:
                case ExpressionType.Subtract:
                case ExpressionType.Multiply:
                case ExpressionType.Divide:
                case ExpressionType.Modulo:
                case ExpressionType.Equal:
                case ExpressionType.NotEqual:
                case ExpressionType.LessThan:
                case ExpressionType.LessThanOrEqual:
                case ExpressionType.GreaterThan:
                case ExpressionType.GreaterThanOrEqual:
                case ExpressionType.And:
                case ExpressionType.Or:
                case ExpressionType.ExclusiveOr:
                case ExpressionType.LeftShift:
                case ExpressionType.RightShift:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
            return BinarySites.GetOrAdd(Tuple.Create(operation, ignoreCase), CreateBinarySite)(left, right);
        }

        private static Func<object?, object?, object?> CreateBinarySite(Tuple<ExpressionType, bool> key)
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSBinaryOperationBinder", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { typeof(ExpressionType), typeof(bool), typeof(bool) }, null)
                ?? throw new NotSupportedException("PowerShell's native binary-operation binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null,
                new object[] { key.Item1, key.Item2, false })!;
            var left = Expression.Parameter(typeof(object), "left");
            var right = Expression.Parameter(typeof(object), "right");
            return Expression.Lambda<Func<object?, object?, object?>>(
                Expression.Dynamic(binder, typeof(object), left, right), left, right).Compile();
        }
    }
}
