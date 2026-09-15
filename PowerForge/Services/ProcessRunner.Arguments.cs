using System.Text;

namespace PowerForge;

public sealed partial class ProcessRunner
{
    // Windows command-line parsing doubles backslashes before a quote, including the
    // closing quote. Keep this algorithm available to tests on every target framework.
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '"' }) < 0)
            return argument;
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
            }
            else
            {
                result.Append('\\', backslashes).Append(character);
            }
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
