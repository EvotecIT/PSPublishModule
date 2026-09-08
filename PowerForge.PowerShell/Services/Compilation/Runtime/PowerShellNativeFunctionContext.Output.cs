namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Applies the native single-expression array operator without a statement-output collector.</summary>
        public object?[] CollectValue(object? value)
        {
            EnsureActive();
            return NativeOutputContract.Shared.Value.Collect(value);
        }

        /// <summary>Applies native implicit-output enumeration and copying to the current compiled success sink.</summary>
        public void WriteOutput(object? value, Action<object?> sink)
        {
            EnsureActive();
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            var contract = NativeOutputContract.Shared.Value;
            // Each output operation owns its adapter. A downstream callback may invoke
            // another function or enter a nested capture without replacing this sink.
            var writer = new SuccessSinkWriter(sink);
            var pipe = contract.CreatePipe();
            contract.ExternalWriter.SetValue(pipe, writer, null);
            try { contract.Write(value, pipe, _executionContext); }
            finally { writer.Close(); }
        }

        private sealed class NativeOutputContract
        {
            internal static readonly Lazy<NativeOutputContract> Shared = new(() => new NativeOutputContract());
            internal readonly Func<object> CreatePipe;
            internal readonly PropertyInfo ExternalWriter;
            internal readonly Action<object?, object, object> Write;
            internal readonly Func<object?, object?[]> Collect;

            private NativeOutputContract()
            {
                var native = PowerShellNativeFunctionHost.NativeContract.Shared;
                var pipeType = native.OutputPipe.FieldType;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
                var constructor = pipeType.GetConstructor(flags | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? throw new NotSupportedException("PowerShell's output-pipe constructor is unavailable.");
                CreatePipe = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor), typeof(object))).Compile();
                ExternalWriter = pipeType.GetProperty("ExternalWriter", flags | BindingFlags.Instance)
                    ?? throw new NotSupportedException("PowerShell's output-pipe writer is unavailable.");
                var binderType = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSPipeWriterBinder", true)!;
                var get = binderType.GetMethod("Get", flags | BindingFlags.Static, null, Type.EmptyTypes, null)
                    ?? throw new NotSupportedException("PowerShell's implicit-output binder is unavailable.");
                var binder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(get, null, Array.Empty<object>())!;
                var value = Expression.Parameter(typeof(object), "value");
                var pipe = Expression.Parameter(typeof(object), "pipe");
                var context = Expression.Parameter(typeof(object), "context");
                Write = Expression.Lambda<Action<object?, object, object>>(
                    Expression.Dynamic(binder, typeof(void), value, Expression.Convert(pipe, pipeType),
                        Expression.Convert(context, native.ExecutionContext.FieldType)), value, pipe, context).Compile();
                var arrayBinderType = typeof(PSObject).Assembly.GetType("System.Management.Automation.Language.PSToObjectArrayBinder", true)!;
                var getArrayBinder = arrayBinderType.GetMethod("Get", flags | BindingFlags.Static, null, Type.EmptyTypes, null)
                    ?? throw new NotSupportedException("PowerShell's array-expression binder is unavailable.");
                var arrayBinder = (CallSiteBinder)PowerShellNativeFunctionHost.Invoke(getArrayBinder, null, Array.Empty<object>())!;
                Collect = Expression.Lambda<Func<object?, object?[]>>(Expression.Dynamic(arrayBinder, typeof(object[]), value), value).Compile();
            }
        }

        /// <summary>Receives already enumerated records from the native pipe synchronously.</summary>
        private sealed class SuccessSinkWriter : PipelineWriter
        {
            private readonly Action<object?> _sink;
            private bool _open = true;

            internal SuccessSinkWriter(Action<object?> sink) => _sink = sink;
            public override bool IsOpen => _open;
            public override int Count => 0;
            public override int MaxCapacity => int.MaxValue;
            public override WaitHandle WaitHandle => throw new NotSupportedException("The synchronous output sink does not expose a wait handle.");
            public override void Close() => _open = false;
            public override void Flush() { }

            public override int Write(object value)
            {
                if (!_open) throw new InvalidOperationException("The synchronous output sink is closed.");
                _sink(value);
                return 1;
            }

            public override int Write(object value, bool enumerateCollection)
            {
                if (enumerateCollection)
                    throw new NotSupportedException("The native output binder owns enumeration before the sink receives a record.");
                return Write(value);
            }
        }
    }
}
