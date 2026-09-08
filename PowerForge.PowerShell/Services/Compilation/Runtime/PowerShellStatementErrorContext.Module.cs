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
            private readonly object? _scope;
            private PSCmdlet? _cmdlet;

            internal ModuleScope(PSCmdlet cmdlet, SessionState state, bool createInvocationScope)
            {
                _contract = NativeContract.Shared;
                _context = _contract.GetRequired(_contract.CmdletContext, cmdlet);
                _previousEngineState = _contract.GetRequired(_contract.EngineSessionState, _context);
                _previousCommandState = _contract.CmdletSessionState.GetValue(cmdlet);
                _state = _contract.GetRequired(_contract.NativeSessionState, state);
                _scope = createInvocationScope ? NativeContract.Invoke(_contract.NewScope, _state, false) : null;
                _cmdlet = cmdlet;
                try
                {
                    if (_scope is not null) _contract.CurrentScope.SetValue(_state, _scope, null);
                    _contract.CmdletSessionState.SetValue(cmdlet, state);
                    _contract.EngineSessionState.SetValue(_context, _state, null);
                    if (createInvocationScope) ApplyBoundPreferences(cmdlet);
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
                    try { _contract.CmdletSessionState.SetValue(cmdlet, _previousCommandState); }
                    finally { _contract.EngineSessionState.SetValue(_context, _previousEngineState, null); }
                }
            }

            private void ApplyBoundPreferences(PSCmdlet cmdlet)
            {
                var bound = cmdlet.MyInvocation.BoundParameters;
                var preferences = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                if (bound.TryGetValue("Verbose", out var verbose))
                    preferences.Add("VerbosePreference", LanguagePrimitives.IsTrue(verbose) ? ActionPreference.Continue : ActionPreference.SilentlyContinue);
                if (bound.TryGetValue("Debug", out var debug))
                    preferences.Add("DebugPreference", LanguagePrimitives.IsTrue(debug)
                        ? (typeof(PSObject).Assembly.GetName().Version!.Major >= 7 ? ActionPreference.Continue : ActionPreference.Inquire)
                        : ActionPreference.SilentlyContinue);
                foreach (var name in new[] { "Warning", "Information", "Error", "Progress" })
                    if (bound.TryGetValue(name + "Action", out var action))
                        preferences.Add(name == "Error" ? "ErrorActionPreference" : name + "Preference", action);
                if (bound.TryGetValue("WhatIf", out var whatIf)) preferences.Add("WhatIfPreference", whatIf);
                if (bound.TryGetValue("Confirm", out var confirm))
                    preferences.Add("ConfirmPreference", LanguagePrimitives.IsTrue(confirm) ? ConfirmImpact.Low : ConfirmImpact.None);
                // Native function locals shadow even inherited AllScope and ReadOnly variables.
                // PSVariable.Set would mutate those shared objects or fail before the body runs.
                if (preferences.Count != 0)
                    _contract.ScopeLocalsTuple.SetValue(_scope, _contract.CreateTuple(preferences), null);
            }
        }
    }
}
