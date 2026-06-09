namespace PowerForgeStudio.Orchestrator.PowerShell;

internal static class PowerShellScriptEscaping
{
    public static string QuoteLiteral(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return "'" + value.Replace("'", "''") + "'";
    }
}
