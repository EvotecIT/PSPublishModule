namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;

    /// <summary>Checks the live storage precondition of a compiler-selected scalar initialization region.</summary>
    internal static class PowerShellRegionLocalOwnership
    {
        /// <summary>Allows detached initialization only when every target is absent from the current local scope.</summary>
        /// <remarks>Existing variables can carry AllScope ownership, constraints, or user-defined behavior. Their authored assignments remain native.</remarks>
        internal static bool CanInitializeLocals(SessionState session, string[] names)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (names.Length == 0) return false;
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || name.IndexOf(':') >= 0) return false;
                if (session.PSVariable.Get("local:" + name) != null) return false;
            }
            return true;
        }
    }
}
