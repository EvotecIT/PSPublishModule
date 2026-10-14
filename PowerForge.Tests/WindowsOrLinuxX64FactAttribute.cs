using System.Runtime.InteropServices;
using Xunit;

namespace PowerForge.Tests;

internal sealed class WindowsOrLinuxX64FactAttribute : FactAttribute
{
    public WindowsOrLinuxX64FactAttribute()
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Windows or Linux x64-only test.";
    }
}
