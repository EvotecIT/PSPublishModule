namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
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
            var lookup = new NativeScriptEntryLookup(powerShell.Runspace.SessionStateProxy.InvokeCommand, entry);
            try
            {
                // CommandInfo dispatch forces cmdlet binding and internal origin in the SDK.
                // Normal file discovery preserves authorization, basic arguments and the root frame.
                powerShell.AddCommand(entry.Path, useLocalScope: false);
                foreach (var argument in arguments)
                {
                    if (argument.Name == null) powerShell.AddArgument(argument.Value);
                    else powerShell.AddParameter(argument.Name, argument.Value);
                }
                return lookup;
            }
            catch { lookup.Dispose(); throw; }
        }

        internal sealed class NativeScriptEntryLookup : IDisposable
        {
            private readonly CommandInvocationIntrinsics _commands;
            private readonly ExternalScriptInfo _entry;
            private readonly EventHandler<CommandLookupEventArgs>? _previous;
            private readonly EventHandler<CommandLookupEventArgs> _installed;
            private ExceptionDispatchInfo? _failure;
            private bool _disposed;

            internal NativeScriptEntryLookup(CommandInvocationIntrinsics commands, ExternalScriptInfo entry)
            {
                _commands = commands;
                _entry = entry;
                _previous = commands.PostCommandLookupAction;
                _installed = Lookup;
                commands.PostCommandLookupAction = _installed;
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
