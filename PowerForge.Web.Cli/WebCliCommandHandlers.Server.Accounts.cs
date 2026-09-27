namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    // Verify passwd identity rather than trusting declarations for an existing account.
    internal static string BuildAccountIdentityCheckCommand(PowerForgeServerAccount account)
    {
        var checks = new List<string>
        {
            $"powerforge_account_record=$(getent passwd {ShellQuote(account.Name ?? string.Empty)})",
            $"test \"$(printf '%s\\n' \"$powerforge_account_record\" | cut -d: -f1)\" = {ShellQuote(account.Name ?? string.Empty)}"
        };
        if (!string.IsNullOrWhiteSpace(account.Home))
            checks.Add($"test \"$(printf '%s\\n' \"$powerforge_account_record\" | cut -d: -f6)\" = {ShellQuote(account.Home)}");
        if (!string.IsNullOrWhiteSpace(account.Shell))
            checks.Add($"test \"$(printf '%s\\n' \"$powerforge_account_record\" | cut -d: -f7)\" = {ShellQuote(account.Shell)}");
        return string.Join(" && ", checks);
    }
}
