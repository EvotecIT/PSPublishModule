namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Globalization;
    using System.Management.Automation;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Uses PowerShell conversion for each exact-match switch input record.</summary>
        public string StringifySwitchValue(object? value)
        {
            EnsureActive();
            return value is null ? string.Empty :
                (string)LanguagePrimitives.ConvertTo(value, typeof(string), CultureInfo.CurrentCulture);
        }
    }
}
