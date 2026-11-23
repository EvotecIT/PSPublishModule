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

        /// <summary>Allows a detached continuation only while every transferred local still has ordinary writable storage and its expected scalar value type.</summary>
        /// <remarks>The retained middle may replace or decorate local storage after the prefix condition ran. Any such change keeps the authored region native.</remarks>
        internal static bool CanUpdateLocals(SessionState session, string[] names, string[] typeNames)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (names == null) throw new ArgumentNullException(nameof(names));
            if (typeNames == null) throw new ArgumentNullException(nameof(typeNames));
            if (names.Length == 0 || names.Length != typeNames.Length) return false;
            for (var index = 0; index < names.Length; index++)
            {
                var name = names[index];
                if (string.IsNullOrEmpty(name) || name.IndexOf(':') >= 0) return false;
                var variable = session.PSVariable.Get("local:" + name);
                if (variable == null || variable.Options != ScopedItemOptions.None || variable.Attributes.Count != 0)
                    return false;
                var expected = ResolveType(typeNames[index]);
                if (expected == null ||
                    (variable.Value == null && expected.IsValueType && Nullable.GetUnderlyingType(expected) == null) ||
                    (variable.Value != null && !expected.IsInstanceOfType(variable.Value)))
                    return false;
            }
            return true;
        }

        private static Type? ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var resolved = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (resolved != null) return resolved;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                resolved = assembly.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (resolved != null) return resolved;
            }
            return null;
        }
    }
}
