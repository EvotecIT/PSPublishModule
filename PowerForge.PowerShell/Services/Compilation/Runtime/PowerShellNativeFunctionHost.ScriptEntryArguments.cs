namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Reflection;
    using System.Runtime.ExceptionServices;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Invokes the prepared native file command directly, preserving ordered argument data and the root frame.</summary>
        internal static NativeScriptEntryLookup AddScriptEntryCommand(PowerShell powerShell, ExternalScriptInfo entry,
            IReadOnlyList<CommandParameter> arguments)
        {
            if (powerShell == null) throw new ArgumentNullException(nameof(powerShell));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            var lookup = new NativeScriptEntryLookup(powerShell.Runspace, entry);
            try
            {
                // CommandInfo dispatch forces cmdlet binding and internal origin in the SDK.
                // Normal file discovery preserves authorization, basic arguments and the root frame.
                powerShell.AddCommand(entry.Path, useLocalScope: false);
                var parameters = powerShell.Commands.Commands[powerShell.Commands.Commands.Count - 1].Parameters;
                foreach (var argument in arguments)
                    parameters.Add(argument);
                return lookup;
            }
            catch { lookup.Dispose(); throw; }
        }

        internal sealed class NativeScriptEntryLookup : IDisposable
        {
            private readonly CommandInvocationIntrinsics _commands;
            private readonly ExternalScriptInfo _entry;
            private readonly object _executionContext;
            private readonly PropertyInfo _rethrowExit;
            private readonly EventHandler<CommandLookupEventArgs>? _previous;
            private readonly EventHandler<CommandLookupEventArgs> _installed;
            private ExceptionDispatchInfo? _failure;
            private bool? _previousExitPolicy;
            private bool _disposed;

            internal NativeScriptEntryLookup(Runspace runspace, ExternalScriptInfo entry)
            {
                _commands = runspace.SessionStateProxy.InvokeCommand;
                _entry = entry;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _executionContext = runspace.GetType().GetProperty("ExecutionContext", flags)?.GetValue(runspace)
                    ?? throw new NotSupportedException("The PowerShell runspace does not expose its native script invocation context.");
                _rethrowExit = _executionContext.GetType().GetProperty("ScriptCommandProcessorShouldRethrowExit", flags)
                    ?? throw new NotSupportedException("The PowerShell runspace does not support native application exit.");
                _previous = _commands.PostCommandLookupAction;
                _installed = Lookup;
                _commands.PostCommandLookupAction = _installed;
            }

            private void Lookup(object? sender, CommandLookupEventArgs lookup)
            {
                var comparison = Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var ownedRoot = string.Equals(lookup.CommandName, _entry.Path, comparison) &&
                    lookup.CommandOrigin == CommandOrigin.Runspace;
                try
                {
                    _previous?.Invoke(sender, lookup);
                    if (!ownedRoot || lookup.Command is not ExternalScriptInfo discovered ||
                        !string.Equals(discovered.Path, _entry.Path, comparison)) return;
                    if (!string.Equals(discovered.ScriptContents, _entry.ScriptContents, StringComparison.Ordinal))
                        throw new InvalidOperationException("The discovered root source differs from its prepared native entry.");
                    // PowerShell consumes this policy when it constructs the entry command.
                    // The entry's exit reaches the process host. Child commands keep their own policy.
                    if (!_previousExitPolicy.HasValue)
                    {
                        _previousExitPolicy = (bool)_rethrowExit.GetValue(_executionContext)!;
                        _rethrowExit.SetValue(_executionContext, true);
                    }
                    lookup.Command = _entry;
                }
                catch (Exception error) when (ownedRoot)
                {
                    // The SDK suppresses thrown lookup handlers and resumes the discovered file.
                    // Return no command normally to stop before binding/body, then report the
                    // original failure at the invocation boundary without masking another error.
                    _failure ??= ExceptionDispatchInfo.Capture(error);
                    lookup.Command = null!;
                }
            }

            internal void Invoke(Action invocation)
            {
                if (invocation == null) throw new ArgumentNullException(nameof(invocation));
                if (_disposed) throw new ObjectDisposedException(nameof(NativeScriptEntryLookup));
                try
                {
                    try { invocation(); }
                    catch (CommandNotFoundException error) when (_failure is not null &&
                        string.Equals(error.CommandName, _entry.Path, Environment.OSVersion.Platform == PlatformID.Win32NT
                            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        _failure.Throw();
                        throw;
                    }
                    _failure?.Throw();
                }
                finally
                {
                    if (_previousExitPolicy is { } original)
                    {
                        _rethrowExit.SetValue(_executionContext, original);
                        _previousExitPolicy = null;
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (ReferenceEquals(_commands.PostCommandLookupAction, _installed))
                    _commands.PostCommandLookupAction = _previous;
            }
        }
    }
}
