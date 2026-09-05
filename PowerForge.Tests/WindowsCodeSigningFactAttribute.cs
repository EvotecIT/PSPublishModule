using Xunit;

namespace PowerForge.Tests;

internal sealed class WindowsCodeSigningFactAttribute : FactAttribute
{
    internal const string ThumbprintEnvironmentVariable = "POWERFORGE_TEST_CODESIGNING_THUMBPRINT";

    public WindowsCodeSigningFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only Authenticode acceptance test.";
        else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ThumbprintEnvironmentVariable)))
            Skip = $"Set {ThumbprintEnvironmentVariable} to a current-user code-signing certificate thumbprint to run live signing acceptance.";
    }
}
