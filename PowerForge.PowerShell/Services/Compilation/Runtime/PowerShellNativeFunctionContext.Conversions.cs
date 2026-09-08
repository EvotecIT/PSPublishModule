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
        private static readonly ConcurrentDictionary<Type, Func<object?, object?>> ConversionSites = new();

        /// <summary>Applies an authored cast while callbacks and stringification use the native invocation.</summary>
        public object? ConvertValue(Type type, object? value)
        {
            EnsureActive();
            return ConversionSites.GetOrAdd(type, CreateConversionSite)(value);
        }

        private static Func<object?, object?> CreateConversionSite(Type destination)
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSConvertBinder", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(Type) }, null)
                ?? throw new NotSupportedException("PowerShell's native conversion binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, new object[] { destination })!;
            var value = Expression.Parameter(typeof(object), "value");
            return Expression.Lambda<Func<object?, object?>>(
                Expression.Convert(Expression.Dynamic(binder, destination, value), typeof(object)), value).Compile();
        }
    }
}
