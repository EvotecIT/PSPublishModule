namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;
    using System.Management.Automation.Runspaces;
    using System.Reflection;

    /// <summary>Uses the native console error formatter after the application pipeline has unwound.</summary>
    internal static class PowerShellPackagedConsoleErrors
    {
        internal static void Write(Runspace runspace, ErrorRecord error)
        {
            try
            {
                // ConsoleHost.ReportException uses this same qualified SMA stream marker.
                // Fall back to the meaningful message if another SDK changes that contract.
                var stream = typeof(PSObject).GetProperty("WriteStream", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (stream is null || !stream.CanWrite || !stream.PropertyType.IsEnum || !Enum.IsDefined(stream.PropertyType, "Error"))
                    throw new NotSupportedException("PowerShell's console error stream marker is unavailable.");
                var wrapped = new PSObject(error);
                stream.SetValue(wrapped, Enum.Parse(stream.PropertyType, "Error"), null);
                using (var formatter = PowerShell.Create())
                {
                    formatter.Runspace = runspace;
                    formatter.AddCommand("Out-Default");
                    formatter.Invoke(new[] { wrapped });
                    if (formatter.HadErrors) Console.Error.WriteLine(error.ToString());
                }
            }
            catch (Exception)
            {
                Console.Error.WriteLine(error.ToString());
            }
        }
    }
}
