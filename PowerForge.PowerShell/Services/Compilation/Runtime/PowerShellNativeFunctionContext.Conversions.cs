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
        private static readonly Lazy<Func<object?, Type, object?>> AsOperation = new(CreateAsOperation);
        private static readonly Lazy<Func<object?, object?>> CustomObjectConversionSite = new(CreateCustomObjectConversionSite);
        private static readonly Lazy<ConstructorInfo> RuntimeTypeNameConstructor = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.Language.TypeName", true)!
            .GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(System.Management.Automation.Language.IScriptExtent), typeof(string) }, null)
            ?? throw new NotSupportedException("PowerShell's authored type-name representation is unavailable."));
        private static readonly Lazy<MethodInfo> ResolveRuntimeTypeName = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.TypeOps", true)!
            .GetMethod("ResolveTypeName", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("PowerShell's native type-name resolver is unavailable."));

        /// <summary>Preserves the authored PSCustomObject alias, including dictionary-to-note-property conversion.</summary>
        public object? ConvertCustomObject(object? value)
        {
            EnsureActive();
            return CustomObjectConversionSite.Value(value);
        }

        /// <summary>Applies PowerShell's nonthrowing value conversion after evaluating both operands.</summary>
        /// <remarks>Invalid destination types still fail through the host's type conversion, as with authored -as.</remarks>
        public object? EvaluateAs(object? value, object? destination)
        {
            EnsureActive();
            var type = (Type)ConvertValue(typeof(Type), destination)!;
            return AsOperation.Value(value, type);
        }

        private static Func<object?, Type, object?> CreateAsOperation()
        {
            var method = typeof(PSObject).Assembly.GetType("System.Management.Automation.TypeOps", true)!
                .GetMethod("AsOperator", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(object), typeof(Type) }, null)
                ?? throw new NotSupportedException("PowerShell's native -as conversion operation is unavailable.");
            var value = Expression.Parameter(typeof(object), "value");
            var destination = Expression.Parameter(typeof(Type), "destination");
            return Expression.Lambda<Func<object?, Type, object?>>(
                Expression.Call(method, value, destination), value, destination).Compile();
        }

        private static Func<object?, object?> CreateCustomObjectConversionSite()
        {
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSCustomObjectConverter", true)!;
            var get = type.GetMethod("Get", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                Type.EmptyTypes, null)
                ?? throw new NotSupportedException("PowerShell's native custom-object conversion binder is unavailable.");
            var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, Array.Empty<object>())!;
            var value = Expression.Parameter(typeof(object), "value");
            return Expression.Lambda<Func<object?, object?>>(
                Expression.Dynamic(binder, typeof(object), value), value).Compile();
        }

        /// <summary>Applies an authored cast while callbacks and stringification use the native invocation.</summary>
        public object? ConvertValue(Type type, object? value)
        {
            EnsureActive();
            // A CLR Object cast is identity, including the native no-output sentinel.
            // Sending that sentinel through PSConvertBinder would create a null record.
            if (type == typeof(object)) return value;
            return ConversionSites.GetOrAdd(type, CreateConversionSite)(value);
        }

        /// <summary>Resolves the authored type name in the loaded PowerShell host before its operand is evaluated.</summary>
        public Type ResolveTypeName(string typeName, string file, int line, int column,
            int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            var authored = RuntimeTypeNameConstructor.Value.Invoke(new object[] { extent, typeName });
            return (Type)PowerShellNativeFunctionHost.Invoke(ResolveRuntimeTypeName.Value, null,
                new[] { authored, extent })!;
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
