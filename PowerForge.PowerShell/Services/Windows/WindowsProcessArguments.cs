using System.Diagnostics;
using System.Text;

namespace PowerForge;

internal static class WindowsProcessArguments
{
    internal static void Add(ProcessStartInfo startInfo, params string[] arguments)
    {
#if NET472
        startInfo.Arguments = string.Join(" ", arguments.Select(EscapeWindowsArgument));
#else
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
#endif
    }

#if NET472
    private static string EscapeWindowsArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return "\"\"";

        var needsQuotes = argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes)
            return argument;

        var builder = new StringBuilder();
        builder.Append('"');

        var backslashCount = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashCount * 2 + 1);
                builder.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount);
                backslashCount = 0;
            }

            builder.Append(ch);
        }

        if (backslashCount > 0)
            builder.Append('\\', backslashCount * 2);

        builder.Append('"');
        return builder.ToString();
    }
#endif
}
