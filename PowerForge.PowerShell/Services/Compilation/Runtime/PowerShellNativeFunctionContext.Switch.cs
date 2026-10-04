namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.Linq.Expressions;
    using System.Management.Automation;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private NativeVariableStorage? _switchStorage;
        private NativeVariableStorage? _switchItemStorage;
        private static readonly Lazy<Func<object?, object, string>> SwitchString = new(CreateSwitchString);
        private static readonly Lazy<Func<ScriptBlock, object?, object?>> SwitchPredicate = new(CreateSwitchPredicateInvocation);

        /// <summary>Saves switch automatic variables before evaluating its input in the current invocation.</summary>
        public NativeSwitchScope EnterSwitch()
        {
            EnsureActive();
            return new NativeSwitchScope(this);
        }

        /// <summary>Owns native enumeration, current-item matching, and restoration for an exact literal switch.</summary>
        public sealed class NativeSwitchScope : IDisposable
        {
            private readonly PowerShellNativeFunctionContext _owner;
            private readonly NativeVariableStorage _iterator, _item;
            private readonly object? _previousIterator, _previousItem;
            private bool _disposed;

            internal NativeSwitchScope(PowerShellNativeFunctionContext owner)
            {
                _owner = owner;
                _iterator = owner._switchStorage ??= new NativeVariableStorage(owner, "switch");
                var automatic = typeof(PSObject).Assembly.GetType("System.Management.Automation.AutomaticVariable", true)!;
                _item = owner._switchItemStorage ??= new NativeVariableStorage(owner, "_",
                    Convert.ToInt32(Enum.Parse(automatic, "Underbar"), CultureInfo.InvariantCulture));
                _previousItem = _item.Read.Invoke(owner.FunctionContext);
                _previousIterator = _iterator.Read.Invoke(owner.FunctionContext);
            }

            /// <summary>The loop cursor stays independent of authored assignments to $switch.</summary>
            public IEnumerator Cursor { get; private set; } = null!;

            /// <summary>Uses PowerShell enumeration; a non-enumerable value, including null, is one switch item.</summary>
            public void Initialize(object? value)
            {
                CheckActive();
                Cursor = NativeEnumerationContract.Shared.Value.GetEnumerator(value) ?? new object?[] { value }.GetEnumerator();
                _iterator.Write.Invoke(_owner.FunctionContext, () => Cursor);
            }

            /// <summary>Installs the advanced cursor's current item before matching any clause.</summary>
            public void SetCurrent()
            {
                CheckActive();
                var current = Cursor.Current;
                _item.Write.Invoke(_owner.FunctionContext, () => current);
            }

            /// <summary>Stringifies the current automatic item separately for each literal clause, as the host does.</summary>
            public bool Matches(string label, bool caseSensitive)
            {
                CheckActive();
                var current = _item.Read.Invoke(_owner.FunctionContext);
                return string.Equals(label, SwitchString.Value(current, _owner._executionContext),
                    caseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase);
            }

            /// <summary>Invokes a compiled predicate in the engine's local scope and converts its captured records as Boolean.</summary>
            public bool MatchesPredicate(ScriptBlock predicate)
            {
                CheckActive();
                if (predicate is null) throw new ArgumentNullException(nameof(predicate));
                var result = SwitchPredicate.Value(predicate, _item.Read.Invoke(_owner.FunctionContext));
                return (bool)_owner.ConvertValue(typeof(bool), result)!;
            }

            /// <summary>Restores iterator then item, including return, failure, and an empty input.</summary>
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _iterator.Write.Invoke(_owner.FunctionContext, () => _previousIterator); }
                finally { _item.Write.Invoke(_owner.FunctionContext, () => _previousItem); }
            }

            private void CheckActive()
            {
                if (_disposed) throw new ObjectDisposedException(nameof(NativeSwitchScope));
                _owner.EnsureActive();
            }
        }

        private static Func<object?, object, string> CreateSwitchString()
        {
            var contextType = typeof(PSObject).Assembly.GetType("System.Management.Automation.ExecutionContext", true)!;
            var value = Expression.Parameter(typeof(object), "value");
            var context = Expression.Parameter(typeof(object), "context");
            return Expression.Lambda<Func<object?, object, string>>(
                Expression.Dynamic(PowerShellNativeLanguageOperations.GetSingletonBinder("PSToStringBinder"),
                typeof(string), value, Expression.Convert(context, contextType)), value, context).Compile();
        }

        private static Func<ScriptBlock, object?, object?> CreateSwitchPredicateInvocation()
        {
            var errorBehavior = typeof(ScriptBlock).GetNestedType("ErrorHandlingBehavior", BindingFlags.NonPublic)
                ?? throw new NotSupportedException("PowerShell's switch predicate error contract is unavailable.");
            var method = typeof(ScriptBlock).GetMethod("DoInvokeReturnAsIs", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(bool), errorBehavior, typeof(object), typeof(object), typeof(object), typeof(object[]) }, null)
                ?? throw new NotSupportedException("PowerShell's switch predicate invocation contract is unavailable.");
            var predicate = Expression.Parameter(typeof(ScriptBlock), "predicate");
            var item = Expression.Parameter(typeof(object), "item");
            var noOutput = Expression.Constant(System.Management.Automation.Internal.AutomationNull.Value, typeof(object));
            return Expression.Lambda<Func<ScriptBlock, object?, object?>>(
                Expression.Call(predicate, method, Expression.Constant(true),
                    Expression.Constant(Enum.Parse(errorBehavior, "WriteToExternalErrorPipe"), errorBehavior),
                    item, noOutput, noOutput, Expression.Constant(null, typeof(object[]))), predicate, item).Compile();
        }

        /// <summary>Recognizes native unlabeled break/continue raised by an invoked command or conversion callback.</summary>
        public bool IsSwitchTransfer(FlowControlException error, bool isBreak)
        {
            EnsureActive();
            var type = typeof(PSObject).Assembly.GetType("System.Management.Automation." + (isBreak ? "BreakException" : "ContinueException"), true)!;
            if (!type.IsInstanceOfType(error)) return false;
            var matches = type.GetMethod("MatchLabel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null)!;
            return (bool)PowerShellNativeFunctionHost.Invoke(matches, error, new object[] { string.Empty })!;
        }

    }
}
