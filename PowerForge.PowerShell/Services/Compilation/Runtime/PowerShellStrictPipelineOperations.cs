namespace PowerForge.Generated.Runtime;

internal static class PowerShellStrictPipelineOperations
{
    internal static int IncrementInt32(int value, int sourceLine)
    {
        if (value != int.MaxValue) return value + 1;
        const string message = "Cannot convert value \"2147483648\" to type \"System.Int32\". Error: \"Value was either too large or too small for an Int32.\"";
        throw new PowerShellScriptCommandError(message, message + Environment.NewLine, sourceLine, new OverflowException());
    }
}
