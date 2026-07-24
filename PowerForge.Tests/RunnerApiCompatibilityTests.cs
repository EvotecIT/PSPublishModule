using System.Reflection;

namespace PowerForge.Tests;

public sealed class RunnerApiCompatibilityTests
{
    [Fact]
    public void ProcessRunRequest_PreservesOriginalConstructorSignature()
    {
        var constructor = typeof(ProcessRunRequest).GetConstructor(
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(IReadOnlyList<string>),
                typeof(TimeSpan),
                typeof(IReadOnlyDictionary<string, string?>),
                typeof(bool),
                typeof(bool)
            });

        Assert.NotNull(constructor);
    }

    [Fact]
    public void PowerShellRunRequest_PreservesOriginalConstructorAndFactorySignatures()
    {
        var constructor = typeof(PowerShellRunRequest).GetConstructor(
            new[]
            {
                typeof(string),
                typeof(IReadOnlyList<string>),
                typeof(TimeSpan),
                typeof(bool),
                typeof(string),
                typeof(IReadOnlyDictionary<string, string?>),
                typeof(string),
                typeof(bool),
                typeof(bool)
            });
        var forCommand = typeof(PowerShellRunRequest).GetMethod(
            nameof(PowerShellRunRequest.ForCommand),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types:
            [
                typeof(string),
                typeof(TimeSpan),
                typeof(bool),
                typeof(string),
                typeof(IReadOnlyDictionary<string, string?>),
                typeof(string),
                typeof(bool),
                typeof(bool)
            ],
            modifiers: null);
        var forCompatibleCommand = typeof(PowerShellRunRequest).GetMethod(
            nameof(PowerShellRunRequest.ForCompatibleCommand),
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types:
            [
                typeof(string),
                typeof(TimeSpan),
                typeof(int),
                typeof(string),
                typeof(IReadOnlyDictionary<string, string?>),
                typeof(string),
                typeof(bool),
                typeof(bool)
            ],
            modifiers: null);

        Assert.NotNull(constructor);
        Assert.NotNull(forCommand);
        Assert.NotNull(forCompatibleCommand);
    }
}
