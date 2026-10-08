using System.Reflection;
using PowerForge;
using PSPublishModule;

namespace PowerForge.Tests;

public sealed class AppleBuildCmdletProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apple_cmdlets_forward_explicit_controlled_source_selection(bool enabled)
    {
        var commands = new object[]
        {
            new NewAppleAppBuildCommand { UseControlledSourceProvenance = enabled },
            new PublishAppleAppToDeviceCommand { UseControlledSourceProvenance = enabled }
        };
        foreach (var command in commands)
        {
            var createRequest = command.GetType().GetMethod("CreateRequest", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var request = Assert.IsAssignableFrom<AppleAppBuildRequest>(createRequest.Invoke(command, new object[] { "Sample.xcodeproj" }));
            Assert.Equal(enabled, request.UseControlledSourceProvenance);
        }
        Assert.False(new NewAppleAppBuildCommand().UseControlledSourceProvenance.IsPresent);
        Assert.False(new PublishAppleAppToDeviceCommand().UseControlledSourceProvenance.IsPresent);
    }
}
