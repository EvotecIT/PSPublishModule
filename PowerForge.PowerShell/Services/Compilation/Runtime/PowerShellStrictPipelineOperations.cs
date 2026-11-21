namespace PowerForge.Generated.Runtime;

internal static class PowerShellStrictPipelineOperations
{
    internal static int IncrementInt32(int value, int sourceLine)
    {
        if (value != int.MaxValue) return value + 1;
        throw Int32Error("2147483648", sourceLine, new OverflowException());
    }

    internal static int ConvertDoubleToInt32(double value, int sourceLine, bool windowsPowerShell)
    {
        try { return Convert.ToInt32(value); }
        catch (OverflowException error)
        {
            throw Int32Error(value.ToString(windowsPowerShell ? "G15" : "G", System.Globalization.CultureInfo.CurrentCulture), sourceLine, error);
        }
    }

    private static PowerShellScriptCommandError Int32Error(string value, int sourceLine, OverflowException error)
    {
        var message = $"Cannot convert value \"{value}\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"";
        return new PowerShellScriptCommandError(message, message + Environment.NewLine, sourceLine, error);
    }
}
