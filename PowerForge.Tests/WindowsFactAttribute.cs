using Xunit;

namespace PowerForge.Tests;

internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
    }
}
