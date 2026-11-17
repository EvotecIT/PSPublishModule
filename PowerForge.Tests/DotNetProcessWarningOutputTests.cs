namespace PowerForge.Tests;

public sealed class DotNetProcessWarningOutputTests
{
    [Fact]
    public void SuccessfulBuildOutput_ReportsEachWarningOnceWithoutVerbose()
    {
        const string compilerWarning =
            @"C:\src\Lib\Widget.cs(14,19): warning CS8618: Non-nullable property 'Name' must contain a non-null value. [C:\src\Lib\Lib.csproj]";
        const string packWarning =
            @"C:\Program Files\dotnet\sdk\10.0.100\Sdks\NuGet.Build.Tasks.Pack\build\NuGet.Build.Tasks.Pack.targets(221,5): warning NU5104: A stable release of a package should not have a prerelease dependency. [C:\src\Lib\Lib.csproj]";
        // MSBuild repeats every warning in its closing summary.
        var stdOut = string.Join(Environment.NewLine,
            "  Determining projects to restore...",
            "  All projects are up-to-date for restore.",
            compilerWarning,
            @"  Lib -> C:\src\Lib\bin\Release\net8.0\Lib.dll",
            packWarning,
            @"  Successfully created package 'C:\src\Artifacts\Lib.1.0.0.nupkg'.",
            "",
            "Build succeeded.",
            "",
            compilerWarning,
            packWarning,
            "    2 Warning(s)",
            "    0 Error(s)");
        var logger = new RecordingLogger();

        DotNetRepositoryReleaseService.LogProcessOutput(logger, "Lib", "dotnet pack", stdOut, string.Empty);

        Assert.Equal(2, logger.Warnings.Count);
        Assert.Single(logger.Warnings, warning => warning.Contains("CS8618", StringComparison.Ordinal));
        Assert.Single(logger.Warnings, warning => warning.Contains("NU5104", StringComparison.Ordinal));
        Assert.All(logger.Warnings, warning => Assert.StartsWith("Lib: dotnet pack: ", warning, StringComparison.Ordinal));
        Assert.Empty(logger.VerboseMessages);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public List<string> VerboseMessages { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message) { }

        public void Success(string message) { }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message) { }

        public void Verbose(string message) => VerboseMessages.Add(message);
    }
}
