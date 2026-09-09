namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Reflection;

    public sealed partial class PowerShellStatementErrorContext
    {
        private static readonly Lazy<CommandRegionContract> CommandRegions = new(() => new CommandRegionContract());

        /// <summary>Streams a hosted region into this invocation's success pipe.</summary>
        internal void InvokeCommandRegion(ScriptBlock script, object?[] arguments)
            => InvokeCommandRegion(script, arguments, _outputPipe);

        /// <summary>Collapses a hosted region's records without moving the inherited error destination.</summary>
        internal object? CaptureCommandRegion(ScriptBlock script, object?[] arguments)
        {
            ThrowIfDisposed();
            var records = new List<object?>();
            var writer = new PowerShellNativeOutput.SuccessSinkWriter(records.Add);
            var output = PowerShellNativeOutput.Shared.Value;
            var pipe = output.CreatePipe();
            output.ExternalWriter.SetValue(pipe, writer, null);
            output.SetTemporaryVariableLists(_outputPipe, pipe);
            try
            {
                InvokeCommandRegion(script, arguments, pipe);
                return records.Count == 0 ? System.Management.Automation.Internal.AutomationNull.Value :
                    records.Count == 1 ? records[0] : records.ToArray();
            }
            finally { writer.Close(); }
        }

        private void InvokeCommandRegion(ScriptBlock script, object?[] arguments, object outputPipe)
        {
            ThrowIfDisposed();
            if (script is null) throw new ArgumentNullException(nameof(script));
            var region = CommandRegions.Value;
            var previousErrorPipe = _contract.ShellErrorPipe.GetValue(_context, null);
            var previousPropagation = _contract.PropagateExceptions.GetValue(_context, null);
            try
            {
                if (_mergeErrorToOutput) _contract.ShellErrorPipe.SetValue(_context, _outputPipe, null);
                else if (_redirectError) _contract.ShellErrorPipe.SetValue(_context, _errorPipe, null);
                if (_handlerDepth != 0) _contract.PropagateExceptions.SetValue(_context, true, null);
                var variables = new List<PSVariable>
                {
                    new PSVariable("ErrorActionPreference", NativeContract.Invoke(_contract.GetTupleValue, _preferenceTuple, 0))
                };
                NativeContract.Invoke(region.InvokeWithPipe, script, true, region.CurrentErrorPipe,
                    null, null, null, outputPipe, null, false, variables, null, arguments);
            }
            finally
            {
                _contract.PropagateExceptions.SetValue(_context, previousPropagation, null);
                _contract.ShellErrorPipe.SetValue(_context, previousErrorPipe, null);
            }
        }

        private sealed class CommandRegionContract
        {
            internal readonly MethodInfo InvokeWithPipe;
            internal readonly object CurrentErrorPipe;

            internal CommandRegionContract()
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var assembly = typeof(PSObject).Assembly;
                var pipe = assembly.GetType("System.Management.Automation.Internal.Pipe", true)!;
                var behavior = typeof(ScriptBlock).GetNestedType("ErrorHandlingBehavior", BindingFlags.NonPublic)
                    ?? throw new NotSupportedException("PowerShell's script error-pipe contract is unavailable.");
                InvokeWithPipe = typeof(ScriptBlock).GetMethod("InvokeWithPipe", flags, null,
                    new[] { typeof(bool), behavior, typeof(object), typeof(object), typeof(object), pipe,
                        typeof(InvocationInfo), typeof(bool), typeof(List<PSVariable>), typeof(Dictionary<string, ScriptBlock>), typeof(object[]) }, null)
                    ?? throw new NotSupportedException("PowerShell's script output-pipe contract is unavailable.");
                CurrentErrorPipe = Enum.Parse(behavior, "WriteToCurrentErrorPipe");
            }
        }
    }
}
