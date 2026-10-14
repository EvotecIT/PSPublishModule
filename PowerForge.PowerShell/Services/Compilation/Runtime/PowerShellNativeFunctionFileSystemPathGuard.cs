namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;

    /// <summary>Checks an already evaluated packaged operand without replacing its value or executing a provider command.</summary>
    public static class PowerShellNativeFunctionFileSystemPathGuard
    {
        /// <summary>Admits an implicit current-location read without adding a path operand or producing pipeline output.</summary>
        /// <param name="session">The active PowerShell session whose current provider owns the implicit read.</param>
        public static void ValidateCurrentLocation(SessionState session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!session.Path.CurrentLocation.Provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("An implicit packaged provider read requires the active FileSystem location.");
        }

        /// <summary>Validates the selected path against the active provider and returns the original operand unchanged.</summary>
        /// <param name="operand">The already evaluated scalar or one-dimensional array passed to the authored command.</param>
        /// <param name="session">The active PowerShell session used to resolve the operand's provider.</param>
        /// <returns>The original operand, preserving its shape, wrappers, ordering, and reference identity.</returns>
        public static object? Validate(object? operand, SessionState session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            var value = operand is PSObject wrapper ? wrapper.BaseObject : operand;
            void Check(object? item)
            {
                var selected = item is PSObject wrapped ? wrapped.BaseObject : item;
                if (selected is null || selected is string { Length: 0 })
                {
                    // Preserve native null/empty binding errors. A command that interprets
                    // an absent operand as its current location is safe only on FileSystem.
                    if (!session.Path.CurrentLocation.Provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("An absent packaged provider path requires the active FileSystem location.");
                    return;
                }
                if (selected is not string path)
                    throw new InvalidOperationException("A guarded packaged path must be a string or a one-dimensional string array.");
                session.Path.GetUnresolvedProviderPathFromPSPath(path, out var provider, out _);
                if (!provider.Name.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A dynamic packaged provider path must resolve to the active FileSystem provider.");
            }
            if (value is Array paths)
            {
                if (paths.Rank != 1) throw new InvalidOperationException("A guarded packaged path array must be one-dimensional.");
                foreach (var path in paths) Check(path);
            }
            else Check(value);
            // Keep scalar/array cardinality, PSObject wrappers, ordering and identity intact.
            // The selected PowerShell command still owns conversion, binding and execution.
            return operand;
        }
    }
}
