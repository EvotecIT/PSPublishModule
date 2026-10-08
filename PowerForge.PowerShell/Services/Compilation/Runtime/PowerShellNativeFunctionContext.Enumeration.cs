namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private NativeVariableStorage? _foreachStorage;

        /// <summary>Saves the invocation's foreach variable before the compiled collection expression runs.</summary>
        public NativeForEachScope EnterForEach()
        {
            EnsureActive();
            return new NativeForEachScope(this);
        }

        /// <summary>Owns foreach iterator visibility and restoration without disposing the authored enumerator.</summary>
        public sealed class NativeForEachScope : IDisposable
        {
            private readonly PowerShellNativeFunctionContext _owner;
            private readonly NativeVariableStorage _storage;
            private readonly object? _previous;
            private bool _disposed;

            internal NativeForEachScope(PowerShellNativeFunctionContext owner)
            {
                _owner = owner;
                _storage = owner._foreachStorage ??= new NativeVariableStorage(owner);
                _previous = _storage.Read.Invoke(owner.FunctionContext);
            }

            /// <summary>Exposes the iterator used by the generated loop, independently of later variable assignments.</summary>
            public IEnumerator? Cursor { get; private set; }

            /// <summary>Applies native enumeration and scalar fallback, then exposes the current iterator.</summary>
            public void Initialize(object? value)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(NativeForEachScope));
                _owner.EnsureActive();
                Cursor = NativeEnumerationContract.Shared.Value.GetEnumerator(value);
                if (Cursor is null && value is not null) Cursor = new object?[] { value }.GetEnumerator();
                _storage.Write.Invoke(_owner.FunctionContext, () => Cursor);
            }

            /// <summary>Restores the enclosing iterator value once, including after failed collection evaluation.</summary>
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _storage.Write.Invoke(_owner.FunctionContext, () => _previous);
            }
        }

        private sealed class NativeEnumerationContract
        {
            internal static readonly Lazy<NativeEnumerationContract> Shared = new(() => new NativeEnumerationContract());
            internal readonly Func<object?, IEnumerator?> GetEnumerator;

            private NativeEnumerationContract()
            {
                var binder = PowerShellNativeLanguageOperations.GetSingletonBinder("PSEnumerableBinder");
                var value = Expression.Parameter(typeof(object), "value");
                GetEnumerator = Expression.Lambda<Func<object?, IEnumerator?>>(
                    Expression.Dynamic(binder, typeof(IEnumerator), value), value).Compile();
            }
        }

        /// <summary>Uses the invocation's tuple or dynamic storage exactly as native iterator statements do.</summary>
        private sealed class NativeVariableStorage
        {
            internal readonly NativeAstOperation Read;
            internal readonly NativeAstOperation Write;

            internal NativeVariableStorage(PowerShellNativeFunctionContext owner, string name = "foreach", int? automaticIndex = null)
            {
                Read = Compile(owner, name, automaticIndex, false);
                Write = Compile(owner, name, automaticIndex, true);
            }

            private static NativeAstOperation Compile(PowerShellNativeFunctionContext owner, string name, int? automaticIndex, bool write)
            {
                const BindingFlags instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                const BindingFlags statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var extent = PowerShellSourceExtent.Create(string.Empty, 1, 1, 1, 2, "0");
                var placeholder = new CommandExpressionAst(extent, new ConstantExpressionAst(extent, 0), null);
                var ast = new ScriptBlockAst(extent, null, new StatementBlockAst(extent, new[] { placeholder }, null), isFilter: false);
                var native = new NativeAstCompiler(owner, ast);
                var tuple = owner._contract.LocalsTuple.GetValue(owner.FunctionContext)!;
                var tupleBase = typeof(PSObject).Assembly.GetType("System.Management.Automation.MutableTuple", true)!;
                var names = (Dictionary<string, int>)tupleBase.GetField("_nameToIndexMap", instance)!.GetValue(tuple)!;
                Expression operation;
                if (automaticIndex.HasValue || owner._optimized && names.ContainsKey(name))
                {
                    var index = automaticIndex ?? names[name];
                    var slot = (Expression)native.Invoke("GetLocal", index)!;
                    operation = write ? Expression.Assign(slot, Expression.Convert(Expression.Invoke(native.RightHandSide), slot.Type)) : slot;
                }
                else
                {
                    var path = Expression.Constant(new VariablePath(name));
                    var method = native.CompilerType.GetMethod(write ? "CallSetVariable" : "CallGetVariable", statics)!;
                    operation = (Expression)PowerShellNativeFunctionHost.Invoke(method, null,
                        write ? new object[] { path, Expression.Invoke(native.RightHandSide), null! } : new object[] { path, null! })!;
                }
                native.Expressions.Add(Expression.Convert(operation, typeof(object)));
                return native.Compile();
            }
        }
    }
}
