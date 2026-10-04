namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;

    public sealed partial class PowerShellStatementErrorContext
    {
        /// <summary>Runs a generated command clause in its module's native preference scope.</summary>
        internal static IDisposable EnterModule(PSCmdlet cmdlet)
            => new ModuleScope(cmdlet, PowerShellModuleSessionState.Resolve(cmdlet), createInvocationScope: true);

        /// <summary>Selects module state while PowerShell creates its own lasting lifecycle scopes.</summary>
        internal static IDisposable EnterModuleBinding(PSCmdlet cmdlet)
            => new ModuleScope(cmdlet, PowerShellModuleSessionState.Resolve(cmdlet), createInvocationScope: false);

        /// <summary>Binds a hosted lifecycle to its module while the native pipeline owns invocation scopes.</summary>
        internal static void BindModule(PSCmdlet cmdlet, ScriptBlock script)
        {
            var contract = NativeContract.Shared;
            var state = PowerShellModuleSessionState.Resolve(cmdlet);
            contract.ScriptBlockSessionState.SetValue(script, contract.GetRequired(contract.NativeSessionState, state), null);
        }

        private sealed class ModuleScope : IDisposable
        {
            private readonly NativeContract _contract;
            private readonly object _context;
            private readonly object _previousEngineState;
            private readonly object? _previousCommandState;
            private readonly object _state;
            private readonly object _previousScope;
            private readonly object? _scope;
            private PSCmdlet? _cmdlet;

            internal ModuleScope(PSCmdlet cmdlet, SessionState state, bool createInvocationScope)
            {
                _contract = NativeContract.Shared;
                _context = _contract.GetRequired(_contract.CmdletContext, cmdlet);
                _previousEngineState = _contract.GetRequired(_contract.EngineSessionState, _context);
                _previousCommandState = _contract.CmdletSessionState.GetValue(cmdlet);
                _state = _contract.GetRequired(_contract.NativeSessionState, state);
                _previousScope = _contract.GetRequired(_contract.CurrentScope, _state);
                BindingFrames.TryGetValue(cmdlet, out var bindingFrame);
                _scope = createInvocationScope
                    ? bindingFrame is not null
                        ? bindingFrame.Scope
                        : NativeContract.Invoke(_contract.NewScope, _state, false)
                    : null;
                _cmdlet = cmdlet;
                try
                {
                    if (_scope is not null) _contract.CurrentScope.SetValue(_state, _scope, null);
                    _contract.CmdletSessionState.SetValue(cmdlet, state);
                    _contract.EngineSessionState.SetValue(_context, _state, null);
                    if (createInvocationScope)
                    {
                        if (bindingFrame is not null) bindingFrame.CompleteInvocationBinding();
                        else ApplyBoundPreferences(cmdlet);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                var cmdlet = _cmdlet;
                if (cmdlet is null) return;
                _cmdlet = null;
                try
                {
                    if (_scope is not null) NativeContract.Invoke(_contract.RemoveScope, _state, _scope);
                }
                finally
                {
                    try { _contract.CurrentScope.SetValue(_state, _previousScope, null); }
                    finally
                    {
                        try { _contract.CmdletSessionState.SetValue(cmdlet, _previousCommandState); }
                        finally { _contract.EngineSessionState.SetValue(_context, _previousEngineState, null); }
                    }
                }
            }

            private void ApplyBoundPreferences(PSCmdlet cmdlet)
            {
                var bound = cmdlet.MyInvocation.BoundParameters;
                var preferences = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in bound)
                    if (TryGetBoundPreference(pair.Key, pair.Value, out var name, out var value)) preferences.Add(name, value);
                // Native function locals shadow even inherited AllScope and ReadOnly variables.
                // PSVariable.Set would mutate those shared objects or fail before the body runs.
                if (preferences.Count != 0)
                    _contract.ScopeLocalsTuple.SetValue(_scope, _contract.CreateTuple(preferences), null);
            }
        }

        private static bool TryGetBoundPreference(string parameter, object value, out string name, out object result)
        {
            name = parameter + "Preference";
            result = value;
            switch (parameter.ToLowerInvariant())
            {
                case "verbose": result = LanguagePrimitives.IsTrue(value) ? ActionPreference.Continue : ActionPreference.SilentlyContinue; return true;
                case "debug": result = LanguagePrimitives.IsTrue(value)
                    ? (typeof(PSObject).Assembly.GetName().Version!.Major >= 7 ? ActionPreference.Continue : ActionPreference.Inquire)
                    : ActionPreference.SilentlyContinue; return true;
                case "erroraction": name = "ErrorActionPreference"; return true;
                case "warningaction": name = "WarningPreference"; return true;
                case "informationaction": name = "InformationPreference"; return true;
                case "progressaction": name = "ProgressPreference"; return true;
                case "whatif": return true;
                case "confirm": result = LanguagePrimitives.IsTrue(value) ? ConfirmImpact.Low : ConfirmImpact.None; return true;
                default: return false;
            }
        }
    }
}
