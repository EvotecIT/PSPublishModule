namespace PowerForge.Generated.Runtime
{
    using System.Globalization;

    /// <summary>Reads the plain input supported by a standalone compiled script.</summary>
    internal static class PowerShellStrictConsoleInput
    {
        internal static string? ReadPlainLine(string prompt, string source, int line, int column, int length,
            string authoredLine, string invokedName, bool windowsPowerShell)
        {
            if (!Console.IsInputRedirected && prompt.Length != 0)
                Console.Out.Write(prompt + ": ");
            try
            {
                return PowerShellPackagedConsoleInput.ReadLine();
            }
            catch (ArgumentOutOfRangeException error) when (error.ParamName == "startIndex")
            {
                var lineNumber = line.ToString(CultureInfo.InvariantCulture);
                var eol = Environment.NewLine;
                var marker = new string('~', Math.Max(1, length));
                var columnPadding = new string(' ', Math.Max(0, column - 1));
                if (windowsPowerShell)
                {
                    var message = "StartIndex cannot be less than zero." + eol + "Parameter name: startIndex";
                    throw new PowerShellScriptCommandError(message,
                        invokedName + " : " + message + eol +
                        $"At {source}:{lineNumber} char:{column.ToString(CultureInfo.InvariantCulture)}" + eol +
                        "+ " + authoredLine + eol + "+ " + columnPadding + marker + eol +
                        "    + CategoryInfo          : NotSpecified: (:) [Read-Host], ArgumentOutOfRangeException" + eol +
                        "    + FullyQualifiedErrorId : System.ArgumentOutOfRangeException,Microsoft.PowerShell.Commands.ReadHostCommand" + eol +
                        " " + eol, line, error);
                }

                var renderedMessage = error.Message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                var linePadding = new string(' ', Math.Max(4, lineNumber.Length) + 1);
                throw new PowerShellScriptCommandError(error.Message,
                    $"Read-Host: {source}:{lineNumber}" + eol +
                    "Line |" + eol + lineNumber.PadLeft(4) + " |  " + authoredLine + eol +
                    linePadding + "|  " + columnPadding + marker + eol +
                    linePadding + "| " + renderedMessage + eol, line, error);
            }
        }
    }
}
