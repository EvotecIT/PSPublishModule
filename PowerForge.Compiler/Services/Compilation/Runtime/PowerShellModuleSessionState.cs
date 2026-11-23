namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Runtime.CompilerServices;

    /// <summary>Resolves the native module state used by compiled command invocations.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class PowerShellModuleSessionState
    {
        private static readonly ConditionalWeakTable<PSModuleInfo, Owner> Owners = new ConditionalWeakTable<PSModuleInfo, Owner>();

        internal static SessionState Resolve(PSCmdlet cmdlet)
        {
            if (cmdlet is null) throw new ArgumentNullException(nameof(cmdlet));
            var module = cmdlet.MyInvocation.MyCommand.Module;
            // Commands installed directly in an initial session state have no module owner.
            return module is null ? cmdlet.SessionState : Owners.GetValue(module, _ => new Owner()).Resolve(module);
        }

        internal static void Register(PSModuleInfo module, SessionState parent)
        {
            if (module is null) throw new ArgumentNullException(nameof(module));
            if (parent is null) throw new ArgumentNullException(nameof(parent));
            Owners.GetValue(module, _ => new Owner()).Register(parent);
        }

        private sealed class Owner
        {
            private readonly object _gate = new object();
            private SessionState? _parent;
            private SessionState? _binaryState;

            internal SessionState Resolve(PSModuleInfo module)
            {
                lock (_gate)
                {
                    if (_parent is not null) return _parent;
                    if (module.SessionState is not null) return module.SessionState;
                    // Native binary modules have no script session state. Let PowerShell
                    // initialize one with module-owned automatic variables and global inheritance.
                    return _binaryState ??= new PSModuleInfo(linkToGlobal: true).SessionState;
                }
            }

            internal void Register(SessionState parent)
            {
                lock (_gate) _parent = parent;
            }
        }
    }
}
