namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Specialized;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;

    public sealed partial class PowerShellNativeFunctionContext
    {
        private static readonly Lazy<MethodInfo> DictionaryEntryOperation = new(() =>
        {
            var operation = typeof(PSObject).Assembly.GetType("System.Management.Automation.HashtableOps", true)!
                .GetMethod("AddKeyValuePair", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null,
                    new[] { typeof(IDictionary), typeof(object), typeof(object), typeof(IScriptExtent) }, null);
            if (operation is null || operation.ReturnType != typeof(void))
                throw new NotSupportedException("PowerShell's positioned dictionary-literal insertion operation is unavailable.");
            return operation;
        });

        /// <summary>Checks the insertion contract before key/value evaluation and creates the native host's literal-map representation.</summary>
        public IDictionary CreateDictionary(bool ordered, int capacity)
        {
            EnsureActive();
            _ = DictionaryEntryOperation.Value;
            var comparer = typeof(PSObject).Assembly.GetName().Version!.Major < 6
                ? StringComparer.InvariantCultureIgnoreCase : StringComparer.OrdinalIgnoreCase;
            return ordered ? (IDictionary)new OrderedDictionary(capacity, comparer) : new Hashtable(capacity, comparer);
        }

        /// <summary>Inserts evaluated key/value objects with PowerShell null/duplicate-key errors at the authored key extent.</summary>
        public void AddDictionaryEntry(IDictionary dictionary, object? key, object? value, string file,
            int line, int column, int endLine, int endColumn, string sourceText)
        {
            EnsureActive();
            var extent = PowerShellSourceExtent.Create(file, line, column, endLine, endColumn, sourceText);
            PowerShellNativeFunctionHost.Invoke(DictionaryEntryOperation.Value, null, new object[] { dictionary, key!, value!, extent });
        }
    }
}
