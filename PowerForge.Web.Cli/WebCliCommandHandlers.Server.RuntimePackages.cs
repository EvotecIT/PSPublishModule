using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    internal static string BuildServerHostPreflightCommand(PowerForgeServerTarget? target)
    {
        var os = string.IsNullOrWhiteSpace(target?.Os) ? "ubuntu" : target.Os.Trim().ToLowerInvariant();
        var version = os.StartsWith("ubuntu-", StringComparison.Ordinal)
            ? os["ubuntu-".Length..]
            : null;
        var checks = new List<string>
        {
            "test -r /etc/os-release",
            ". /etc/os-release",
            "test \"$ID\" = 'ubuntu'"
        };

        if (!string.IsNullOrWhiteSpace(version))
            checks.Add($"test \"$VERSION_ID\" = {ShellQuote(version)}");

        var architecture = NormalizeLinuxArchitecture(target?.Architecture);
        if (!string.IsNullOrWhiteSpace(architecture))
            checks.Add($"test \"$(uname -m)\" = {ShellQuote(architecture)}");

        return string.Join(" && ", checks);
    }

    internal static string[] GetDeclaredRuntimePackageNames(PowerForgeServerPackages? packages)
    {
        if (packages is null)
            return Array.Empty<string>();

        var names = GetDeclaredDotnetSdkPackageNames(packages.DotnetSdks).ToList();
        if (packages.Powershell)
            names.Add("powershell");

        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static string[] GetDeclaredPackageNames(PowerForgeServerPackages? packages)
        => (packages?.Apt ?? Array.Empty<string>())
            .Concat(GetDeclaredRuntimePackageNames(packages))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    internal static string[] GetDeclaredDotnetSdkPackageNames(IEnumerable<string>? versions)
    {
        var names = new List<string>();
        foreach (var version in versions ?? Array.Empty<string>())
        {
            if (!TryNormalizeDotnetSdkVersion(version, out var normalized))
                throw new InvalidOperationException($"Unsupported .NET SDK version '{version}'.");
            names.Add($"dotnet-sdk-{normalized}");
        }
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static bool TryNormalizeDotnetSdkVersion(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            return false;

        normalized = value switch
        {
            "8" or "8.0" => "8.0",
            "10" or "10.0" => "10.0",
            _ => string.Empty
        };
        return normalized.Length > 0;
    }

    internal static string BuildMicrosoftPackageRepositoryInstallCommand()
        => string.Join('\n',
            "apt-get update",
            "apt-get install -y ca-certificates curl",
            ". /etc/os-release",
            "test \"$ID\" = 'ubuntu'",
            "powerforge_ms_repo=$(mktemp --suffix=.deb)",
            "powerforge_ms_repo_cleanup() { rm -f -- \"$powerforge_ms_repo\"; }",
            "trap powerforge_ms_repo_cleanup EXIT",
            "trap 'exit 129' HUP",
            "trap 'exit 130' INT",
            "trap 'exit 143' TERM",
            "curl -fsSL \"https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb\" -o \"$powerforge_ms_repo\"",
            "dpkg -i \"$powerforge_ms_repo\"",
            "powerforge_ms_repo_cleanup",
            "trap - EXIT HUP INT TERM",
            "apt-get update");

    internal static string BuildPowerShellInstallCommand()
        => "apt-get install -y powershell";

    internal static string? NormalizeLinuxArchitecture(string? architecture)
        => architecture?.Trim().ToLowerInvariant() switch
        {
            null or "" => null,
            "x64" or "amd64" or "x86_64" => "x86_64",
            "arm64" or "aarch64" => "aarch64",
            _ => architecture.Trim()
        };

    internal static bool IsSafeAptPackageName(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
           value.Length <= 128 &&
           IsLowerAsciiLetterOrDigit(value[0]) &&
           value.All(static character => IsLowerAsciiLetterOrDigit(character) || character is '+' or '-' or '.' or ':');

    private static bool IsLowerAsciiLetterOrDigit(char character)
        => character is >= 'a' and <= 'z' or >= '0' and <= '9';

    internal static bool IsSafeApacheModuleName(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
           value.Length <= 64 &&
           (char.IsAsciiLetterOrDigit(value[0])) &&
           value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
}
