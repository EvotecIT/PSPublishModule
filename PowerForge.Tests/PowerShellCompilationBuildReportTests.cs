using System;
using System.Linq;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationBuildReportTests
{
    // Shape of `dotnet publish --verbosity minimal` output when the generated project fails: progress lines,
    // a warning, the errors, and MSBuild's closing summary that repeats them with the project in brackets.
    private const string FailedBuildOutput = """
          Determining projects to restore...
          Restored C:\work\gen\Tool.csproj (in 1.2 sec).
        C:\work\gen\CompiledPowerShellScript.cs(41,17): warning CS8600: Converting null literal or possible null value to non-nullable type. [C:\work\gen\Tool.csproj]
        C:\work\gen\CompiledPowerShellScript.cs(57,9): error CS0103: The name 'missing' does not exist in the current context [C:\work\gen\Tool.csproj]
        C:\work\gen\Program.cs(12,5): error CS1002: ; expected [C:\work\gen\Tool.csproj::TargetFramework=net10.0]

        Build FAILED.

        C:\work\gen\CompiledPowerShellScript.cs(41,17): warning CS8600: Converting null literal or possible null value to non-nullable type. [C:\work\gen\Tool.csproj]
        C:\work\gen\CompiledPowerShellScript.cs(57,9): error CS0103: The name 'missing' does not exist in the current context [C:\work\gen\Tool.csproj]
        C:\work\gen\Program.cs(12,5): error CS1002: ; expected [C:\work\gen\Tool.csproj::TargetFramework=net10.0]
            1 Warning(s)
            2 Error(s)
        """;

    [Fact]
    public void FormatFailure_KeepsEachErrorLineOnceAndDropsProgressNoise()
    {
        var result = new PowerShellCompilationBuildResult
        {
            Error = "Generated .NET build failed with exit code 1.",
            BuildOutput = FailedBuildOutput,
            Failure = new PowerShellCompilationFailure { Stage = PowerShellCompilationFailureStage.Build }
        };

        var lines = PowerShellCompilationBuildReport.FormatFailure(result, "full output hint")
            .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        Assert.Equal(new[]
        {
            "PowerShell artifact build failed during Build: Generated .NET build failed with exit code 1.",
            "Build errors (2):",
            @"  C:\work\gen\CompiledPowerShellScript.cs(57,9): error CS0103: The name 'missing' does not exist in the current context",
            @"  C:\work\gen\Program.cs(12,5): error CS1002: ; expected",
            "full output hint"
        }, lines);
    }

    [Fact]
    public void FormatFailure_BoundsRestoreErrorsEmbeddedInTheErrorMessage()
    {
        // A failed restore puts its output in the error message rather than in BuildOutput.
        var restoreOutput = string.Join(Environment.NewLine, Enumerable.Range(1, 5)
            .Select(index => $@"C:\work\gen\Tool.csproj : error NU1101: Unable to find package Missing.Package{index}. [C:\work\gen\Tool.csproj]"));
        var result = new PowerShellCompilationBuildResult
        {
            Error = "Generated .NET restore failed with exit code 1." + Environment.NewLine + restoreOutput
        };

        var lines = PowerShellCompilationBuildReport.FormatFailure(result, maximumItems: 2)
            .Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        Assert.Equal(new[]
        {
            "PowerShell artifact build failed: Generated .NET restore failed with exit code 1.",
            "Build errors (5):",
            @"  C:\work\gen\Tool.csproj : error NU1101: Unable to find package Missing.Package1.",
            @"  C:\work\gen\Tool.csproj : error NU1101: Unable to find package Missing.Package2.",
            "  ... and 3 more error line(s)"
        }, lines);
    }
}
