namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public sealed partial class PowerShellStatementErrorContext
    {
        private static readonly ConditionalWeakTable<PSCmdlet, BindingFrame> BindingFrames = new();

        /// <summary>Gives script argument conversion the callee scope that native function binding creates.</summary>
        internal static IDisposable EnterStringParameterBinding(EngineIntrinsics engine, bool advancedFunction)
        {
            var contract = NativeContract.Shared;
            var callerState = contract.GetRequired(contract.NativeSessionState, engine.SessionState);
            var context = contract.GetRequired(contract.SessionExecutionContext, callerState);
            var processor = contract.CurrentCommandProcessor.GetValue(context, null);
            if (processor is null || contract.ProcessorCommand.GetValue(processor, null) is not PSCmdlet cmdlet ||
                cmdlet.GetType().Assembly != typeof(PowerShellStatementErrorContext).Assembly)
                return EmptyBindingScope.Instance;
            var frame = BindingFrames.GetValue(cmdlet, command => new BindingFrame(command, contract, advancedFunction));
            return new BindingActivation(frame);
        }

        private sealed class BindingFrame
        {
            internal readonly PSCmdlet Cmdlet;
            internal readonly NativeContract Contract;
            internal readonly SessionState Session;
            internal readonly object State, Scope, Context;
            private readonly HashSet<string> _bound = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, PropertyInfo> _parameters = new(StringComparer.OrdinalIgnoreCase);
            private readonly object? _tuple;
            private readonly Dictionary<string, int> _tupleIndices;

            internal BindingFrame(PSCmdlet cmdlet, NativeContract contract, bool advancedFunction)
            {
                Cmdlet = cmdlet;
                Contract = contract;
                Session = PowerShellModuleSessionState.Resolve(cmdlet);
                State = contract.GetRequired(contract.NativeSessionState, Session);
                Context = contract.GetRequired(contract.CmdletContext, cmdlet);
                Scope = NativeContract.Invoke(contract.NewScope, State, false)!;
                var variables = (IDictionary<string, PSVariable>)contract.GetRequired(contract.ScopeVariables, Scope);
                variables["PSBoundParameters"] = new PSVariable("PSBoundParameters", cmdlet.MyInvocation.BoundParameters);
                variables["MyInvocation"] = new PSVariable("MyInvocation", cmdlet.MyInvocation);
                if (advancedFunction) variables["PSCmdlet"] = new PSVariable("PSCmdlet", cmdlet);
                var fields = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in cmdlet.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (property.CanRead && property.CanWrite && property.IsDefined(typeof(ParameterAttribute), false))
                    {
                        _parameters.Add(property.Name, property);
                        if (!property.IsDefined(typeof(ValidateArgumentsAttribute), false)) fields.Add(property.Name, property.PropertyType);
                    }
                if (advancedFunction)
                    foreach (var name in new[] { "VerbosePreference", "DebugPreference", "WarningPreference", "InformationPreference",
                        "ErrorActionPreference", "WhatIfPreference", "ConfirmPreference", "ProgressPreference" })
                        if (!fields.ContainsKey(name)) fields.Add(name, typeof(object));
                if (fields.Count == 0) _tupleIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                else
                {
                    _tuple = contract.CreateUninitializedTuple(fields, out _tupleIndices);
                    contract.ScopeLocalsTuple.SetValue(Scope, _tuple, null);
                }
            }

            internal void CaptureBoundParameters()
            {
                var variables = (IDictionary<string, PSVariable>)Contract.GetRequired(Contract.ScopeVariables, Scope);
                foreach (var pair in Cmdlet.MyInvocation.BoundParameters)
                {
                    if (!_parameters.ContainsKey(pair.Key) || !_bound.Add(pair.Key)) continue;
                    if (_parameters.TryGetValue(pair.Key, out var property))
                    {
                        if (_tupleIndices.TryGetValue(pair.Key, out var index))
                        {
                            NativeContract.Invoke(Contract.SetTupleValue, _tuple, index, pair.Value);
                            continue;
                        }
                        var variable = new PSVariable(pair.Key, pair.Value);
                        foreach (var attribute in property.GetCustomAttributes(typeof(ParameterAttribute), false))
                            variable.Attributes.Add((Attribute)attribute);
                        variable.Attributes.Add((Attribute)NativeContract.Construct(Contract.ArgumentConverterConstructor, new object[] { new[] { property.PropertyType } }));
                        foreach (var attribute in property.GetCustomAttributes(typeof(ValidateArgumentsAttribute), false))
                            variable.Attributes.Add((Attribute)attribute);
                        variables[pair.Key] = variable;
                    }
                }
            }

            internal void CompleteInvocationBinding()
            {
                CaptureBoundParameters();
                var variables = (IDictionary<string, PSVariable>)Contract.GetRequired(Contract.ScopeVariables, Scope);
                // Native functions apply their bound common preferences after argument conversion.
                // Unbound preferences retain any mutations made by a conversion callback.
                foreach (var pair in Cmdlet.MyInvocation.BoundParameters)
                    if (TryGetBoundPreference(pair.Key, pair.Value, out var name, out var value))
                    {
                        if (_tupleIndices.TryGetValue(name, out var index)) NativeContract.Invoke(Contract.SetTupleValue, _tuple, index, value);
                        else variables[name] = new PSVariable(name, value);
                    }
            }

            internal void WriteBackBoundParameters()
            {
                var variables = (IDictionary<string, PSVariable>)Contract.GetRequired(Contract.ScopeVariables, Scope);
                foreach (var name in _bound)
                    if (_parameters.TryGetValue(name, out var property))
                    {
                        if (_tupleIndices.TryGetValue(name, out var index))
                            property.SetValue(Cmdlet, NativeContract.Invoke(Contract.GetTupleValue, _tuple, index), null);
                        else if (variables.TryGetValue(name, out var variable)) property.SetValue(Cmdlet, variable.Value, null);
                    }
            }

        }

        private sealed class BindingActivation : IDisposable
        {
            private BindingFrame? _frame;
            private readonly object _engineState, _scope;
            private readonly object? _commandState;

            internal BindingActivation(BindingFrame frame)
            {
                _frame = frame;
                var contract = frame.Contract;
                _engineState = contract.GetRequired(contract.EngineSessionState, frame.Context);
                _scope = contract.GetRequired(contract.CurrentScope, frame.State);
                _commandState = contract.CmdletSessionState.GetValue(frame.Cmdlet);
                try
                {
                    contract.CurrentScope.SetValue(frame.State, frame.Scope, null);
                    contract.CmdletSessionState.SetValue(frame.Cmdlet, frame.Session);
                    contract.EngineSessionState.SetValue(frame.Context, frame.State, null);
                    frame.CaptureBoundParameters();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                var frame = _frame;
                if (frame is null) return;
                _frame = null;
                try { frame.WriteBackBoundParameters(); }
                finally
                {
                    try { frame.Contract.CurrentScope.SetValue(frame.State, _scope, null); }
                    finally
                    {
                        try { frame.Contract.CmdletSessionState.SetValue(frame.Cmdlet, _commandState); }
                        finally { frame.Contract.EngineSessionState.SetValue(frame.Context, _engineState, null); }
                    }
                }
            }
        }

        private sealed class EmptyBindingScope : IDisposable
        {
            internal static readonly EmptyBindingScope Instance = new();
            public void Dispose() { }
        }
    }
}
