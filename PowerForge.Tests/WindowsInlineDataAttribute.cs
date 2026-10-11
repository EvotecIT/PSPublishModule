using System.Reflection;
using Xunit.Sdk;

namespace PowerForge.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class WindowsInlineDataAttribute : DataAttribute
{
    private readonly object[] _data;

    public WindowsInlineDataAttribute(params object[] data)
    {
        _data = data;
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only test.";
    }

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        yield return _data;
    }
}
