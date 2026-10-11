using System.Runtime.InteropServices;
using Xunit;

namespace PowerForge.Tests;

internal sealed class WindowsX64FactAttribute : FactAttribute
{
    public WindowsX64FactAttribute()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Windows x64-only test.";
    }
}
