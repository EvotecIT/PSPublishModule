using System;
using System.Collections.Generic;
using Xunit;

namespace PowerForge.Tests;

public sealed class DotNetPublishPipelineRunnerPublishPropertyTests
{
    [Fact]
    public void BuildPublishMsBuildProperties_MergesGlobalTargetAndStyleOverrideProperties()
    {
        var global = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PublishSingleFile"] = "true",
            ["GlobalOnly"] = "1"
        };

        var publish = new DotNetPublishPublishOptions
        {
            MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetOnly"] = "2",
                ["PublishSingleFile"] = "true"
            },
            StyleOverrides = new Dictionary<string, DotNetPublishStyleOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["PortableCompat"] = new DotNetPublishStyleOverride
                {
                    MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PublishSingleFile"] = "false",
                        ["StyleOnly"] = "3"
                    }
                }
            }
        };

        var merged = DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(global, publish, DotNetPublishStyle.PortableCompat);

        Assert.Equal("1", merged["GlobalOnly"]);
        Assert.Equal("2", merged["TargetOnly"]);
        Assert.Equal("3", merged["StyleOnly"]);
        Assert.Equal("false", merged["PublishSingleFile"]);
    }
}
