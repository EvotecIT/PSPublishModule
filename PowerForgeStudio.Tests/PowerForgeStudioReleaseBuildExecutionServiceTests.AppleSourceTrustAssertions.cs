namespace PowerForgeStudio.Tests;

public sealed partial class PowerForgeStudioReleaseBuildExecutionServiceTests
{
    private static FileNotFoundException AssertMissingExactSourceInput(Action action)
    {
        var exception = Record.Exception(action);
        Assert.NotNull(exception);
        var wrapper = Assert.IsType<InvalidOperationException>(exception);
        Assert.StartsWith("Apple source trust validation failed:", wrapper.Message, StringComparison.Ordinal);
        var cause = wrapper.InnerException;
        while (cause?.InnerException is not null)
            cause = cause.InnerException;
        return Assert.IsType<FileNotFoundException>(cause);
    }
}
