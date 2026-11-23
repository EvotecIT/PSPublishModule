namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Reflection;
    using System.Threading;

    /// <summary>Shared native record enumeration and synchronous pipe adapters for generated command hosts.</summary>
    internal static class PowerShellNativeOutput
    {
        internal static readonly Lazy<Contract> Shared = new(() => new Contract());

        internal static void Write(object? value, Action<object?> sink, object executionContext)
        {
            if (sink is null) throw new ArgumentNullException(nameof(sink));
            var contract = Shared.Value;
            // Each operation owns its adapter so a downstream callback can enter
            // another compiled function or capture without replacing this sink.
            var writer = new SuccessSinkWriter(sink);
            var pipe = contract.CreatePipe();
            contract.ExternalWriter.SetValue(pipe, writer, null);
            try { contract.Write(value, pipe, executionContext); }
            finally { writer.Close(); }
        }

        internal sealed class Contract
        {
            internal readonly Func<object> CreatePipe;
            internal readonly PropertyInfo ExternalWriter;
            internal readonly Action<object, object> SetTemporaryVariableLists;
            internal readonly Action<object?, object, object> Write;
            internal readonly Action<object, object?> AddRecord;
            internal readonly Func<object?, object?[]> Collect;

            internal Contract()
            {
                var assembly = typeof(PSObject).Assembly;
                var pipeType = assembly.GetType("System.Management.Automation.Internal.Pipe", true)!;
                var contextType = assembly.GetType("System.Management.Automation.ExecutionContext", true)!;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic;
                var constructor = pipeType.GetConstructor(flags | BindingFlags.Instance, null, Type.EmptyTypes, null)
                    ?? throw new NotSupportedException("PowerShell's output-pipe constructor is unavailable.");
                CreatePipe = Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(constructor), typeof(object))).Compile();
                ExternalWriter = pipeType.GetProperty("ExternalWriter", flags | BindingFlags.Instance)
                    ?? throw new NotSupportedException("PowerShell's output-pipe writer is unavailable.");
                var setLists = pipeType.GetMethod("SetVariableListForTemporaryPipe", flags | BindingFlags.Instance,
                    null, new[] { pipeType }, null)
                    ?? throw new NotSupportedException("PowerShell's temporary-pipe variable-list operation is unavailable.");
                var add = pipeType.GetMethod("Add", flags | BindingFlags.Instance, null, new[] { typeof(object) }, null)
                    ?? throw new NotSupportedException("PowerShell's output-pipe record operation is unavailable.");
                var value = Expression.Parameter(typeof(object), "value");
                var pipe = Expression.Parameter(typeof(object), "pipe");
                var temporaryPipe = Expression.Parameter(typeof(object), "temporaryPipe");
                var context = Expression.Parameter(typeof(object), "context");
                SetTemporaryVariableLists = Expression.Lambda<Action<object, object>>(
                    Expression.Call(Expression.Convert(pipe, pipeType), setLists, Expression.Convert(temporaryPipe, pipeType)),
                    pipe, temporaryPipe).Compile();
                AddRecord = Expression.Lambda<Action<object, object?>>(
                    Expression.Call(Expression.Convert(pipe, pipeType), add, value), pipe, value).Compile();
                Write = Expression.Lambda<Action<object?, object, object>>(
                    Expression.Dynamic(PowerShellNativeLanguageOperations.GetSingletonBinder("PSPipeWriterBinder"), typeof(void), value, Expression.Convert(pipe, pipeType),
                        Expression.Convert(context, contextType)), value, pipe, context).Compile();
                Collect = Expression.Lambda<Func<object?, object?[]>>(
                    Expression.Dynamic(PowerShellNativeLanguageOperations.GetSingletonBinder("PSToObjectArrayBinder"), typeof(object[]), value), value).Compile();
            }
        }

        /// <summary>Receives already enumerated records from the native pipe synchronously.</summary>
        internal sealed class SuccessSinkWriter : PipelineWriter
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
