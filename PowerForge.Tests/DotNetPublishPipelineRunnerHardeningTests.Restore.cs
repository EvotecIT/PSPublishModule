using PowerForge;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerHardeningTests
{
    [Fact]
    public void TrustedGitEnvironment_EnablesWindowsLongPaths()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Dictionary<string, string?> environment =
            DotNetPublishPipelineRunner.CreateTrustedGitEnvironment();
        int count = int.Parse(
            environment["GIT_CONFIG_COUNT"]!,
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains(
            Enumerable.Range(0, count),
            index => string.Equals(
                         environment["GIT_CONFIG_KEY_" + index],
                         "core.longpaths",
                         StringComparison.Ordinal) &&
                     string.Equals(
                         environment["GIT_CONFIG_VALUE_" + index],
                         "true",
                         StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRestoreArguments_PreservesExplicitRuntimeMatrix()
    {
        var plan = new DotNetPublishPlan
        {
            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RestoreLockedMode"] = "true",
                ["RuntimeIdentifiers"] = "linux-x64;osx-arm64;win-x64"
            },
            Targets =
            [
                new DotNetPublishTargetPlan
                {
                    ProjectPath = "Studio.csproj",
                    Combinations =
                    [
                        new DotNetPublishTargetCombination
                        {
                            Framework = "net10.0",
                            Runtime = "win-x64",
                            Style = DotNetPublishStyle.PortableCompat
                        }
                    ]
                }
            ]
        };

        List<string> arguments = DotNetPublishPipelineRunner.BuildRestoreArguments(
            plan,
            "Studio.csproj",
            "win-x64",
            "net10.0");

        Assert.Contains(
            "/p:RuntimeIdentifiers=\"linux-x64;osx-arm64;win-x64\"",
            arguments);
        Assert.Contains("/p:RestoreLockedMode=true", arguments);
        Assert.DoesNotContain("-r", arguments);
    }

    [Fact]
    public void SdkEvidenceArguments_PreserveExplicitRuntimeMatrix()
    {
        using JsonDocument properties = JsonDocument.Parse("{}");
        var arguments = new List<string>();

        DotNetPublishPipelineRunner.AppendSdkEvidenceProperties(
            arguments,
            properties.RootElement,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RuntimeIdentifiers"] = "linux-x64;osx-arm64;win-x64",
                ["TargetFramework"] = "net10.0"
            });

        Assert.Contains(
            "-p:RuntimeIdentifiers=\"linux-x64;osx-arm64;win-x64\"",
            arguments);
        Assert.Contains("-p:TargetFramework=net10.0", arguments);
    }
}
