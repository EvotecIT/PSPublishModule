using System.Text;

namespace PowerForge;

internal static class PowerForgeWixInstallerFormatting
{
    internal static string ToWixScope(PowerForgeInstallerScope scope)
    {
        return scope == PowerForgeInstallerScope.PerUser ? "perUser" : "perMachine";
    }

    internal static string ResolveInstallRootDirectoryId(PowerForgeInstallerScope scope)
    {
        return scope == PowerForgeInstallerScope.PerUser ? "LocalAppDataFolder" : "ProgramFiles64Folder";
    }

    internal static string EscapeFormattedText(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '[')
                builder.Append(@"[\[]");
            else if (ch == ']')
                builder.Append(@"[\]]");
            else
                builder.Append(ch);
        }

        return builder.ToString();
    }
}
