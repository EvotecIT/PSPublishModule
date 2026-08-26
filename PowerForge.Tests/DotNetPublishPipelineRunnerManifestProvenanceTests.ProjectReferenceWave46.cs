using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("property")]
    [InlineData("item")]
    [InlineData("metadata")]
    [InlineData("dynamic-property")]
    [InlineData("imported-property")]
    public void ControlledBuildInputs_RejectValueProducingTaskPropertyFunction(string sourceKind)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string unsafeSource = sourceKind switch
            {
                "direct" => "$([System.String]::Copy('x/tmp/payload.dll').Substring(1))",
                "property" => "$(PayloadPath)",
                "item" => "@(PayloadPath)",
                "metadata" => "%(PayloadPath.Path)",
                "dynamic-property" => "$($(PropertyName))",
                "imported-property" => "$(PayloadPath)",
                _ => throw new InvalidOperationException("Unknown source kind.")
            };
            string definition = sourceKind switch
            {
                "property" => "<PropertyGroup><PayloadPath>$([System.String]::Copy('x/tmp/payload.dll').Substring(1))</PayloadPath></PropertyGroup>",
                "item" => "<ItemGroup><PayloadPath Include=\"$([System.String]::Copy('x/tmp/payload.dll').Substring(1))\" /></ItemGroup>",
                "metadata" => "<ItemGroup><PayloadPath Include=\"payload\"><Path>$([System.String]::Copy('x/tmp/payload.dll').Substring(1))</Path></PayloadPath></ItemGroup>",
                "dynamic-property" => "<PropertyGroup><PropertyName>PayloadPath</PropertyName><PayloadPath>$([System.String]::Copy('x/tmp/payload.dll').Substring(1))</PayloadPath></PropertyGroup>",
                _ => string.Empty
            };
            if (sourceKind.Equals("imported-property", StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(root, "Directory.Build.props"),
                    "<Project><PropertyGroup><PayloadPath>$([System.String]::Copy('x/tmp/payload.dll').Substring(1))</PayloadPath></PropertyGroup></Project>");
            }
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                $$"""
                <Project>
                  {{definition}}
                  <Target Name="CopyPayload" BeforeTargets="Build">
                    <Copy SourceFiles="{{unsafeSource}}" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));

            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                """
                <Project>
                  <Target Name="CopyPayload" BeforeTargets="$([System.String]::Copy('Build'))">
                    <Copy SourceFiles="artifacts/payload.dll" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("-logger:tools/ControlledLogger.dll")]
    [InlineData("/distributedLogger:tools/ControlledLogger.dll")]
    [InlineData("@tools/secondary-options.txt")]
    public void ControlledBuildInputs_RejectExecutableResponseFileSwitch(string value)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string responsePath = Path.Combine(root, "Directory.Build.rsp");
            File.WriteAllText(responsePath, value);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));

            File.WriteAllText(responsePath, "-p:Configuration=Release");
            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ADDITIONAL_DEPS")]
    [InlineData("DOTNET_SHARED_STORE")]
    [InlineData("CORECLR_ENABLE_PROFILING")]
    [InlineData("CORECLR_PROFILER_PATH_64")]
    [InlineData("COR_ENABLE_PROFILING")]
    [InlineData("NUGET_PLUGIN_PATHS")]
    public void ControlledBuildEnvironment_RejectsRuntimeInjectionVariable(string variableName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.False(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    [variableName] = Path.Combine(root, "tools", "injection.dll")
                },
                root,
                controlledRoot,
                out _));

            Assert.False(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["ProductSetting"] = "controlled"
                },
                root,
                controlledRoot,
                out _));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildSafeguards_DisableAutomaticResponseFiles()
    {
        var arguments = new List<string>();

        DotNetPublishPipelineRunner.AppendControlledProofSafeguards(
            arguments,
            "isolated.config",
            "isolated-source",
            "isolated.lock.json");

        Assert.Contains("-noAutoResponse", arguments);
    }
}
