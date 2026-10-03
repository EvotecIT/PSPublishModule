using PowerForge;

namespace PowerForge.Tests;

public sealed partial class AppleDeviceDeploymentServiceTests
{
    [Theory]
    [InlineData("00008150-00011C2A3640401C", "UDID")]
    [InlineData("312442601095196", "ECID")]
    [InlineData("3DA86114-A96C-5109-970A-B52EA186B0E9", "CoreDevice ID")]
    public async Task GetDevicesAsync_parses_xcode27_device_without_hostname(string identifier, string identifierKind)
    {
        var output = $"""
Name       Hostname   Identifier                                    State                Model                            Reality
--------   --------   -------------------------------------------   ------------------   ------------------------------   --------
EvoPhone              {identifier} ({identifierKind})   available (paired)   iPhone 17 Pro Max (iPhone18,2)   physical
OldPhone              00008150-00011C2A3640402C (UDID)   unavailable          iPhone 15                        physical
""";
        var runner = new CapturingProcessRunner(_ => Success(output));
        var service = new AppleDeviceDeploymentService(runner);
        var devices = await service.GetDevicesAsync(new AppleDeviceListRequest { Device = "EvoPhone" });

        var device = Assert.Single(devices);
        Assert.Equal("EvoPhone", device.Name);
        Assert.Equal(identifier, device.Identifier);
        Assert.Equal(string.Empty, device.Hostname);
        Assert.Equal("available (paired)", device.State);
        Assert.Equal("iPhone 17 Pro Max (iPhone18,2)", device.Model);
    }

    [Fact]
    public async Task GetDevicesAsync_parses_xcode27_connected_device_with_hostname()
    {
        var output = """
EvoPhone   EvoPhone.coredevice.local   00008150-00011C2A3640401C (UDID)   connected   iPhone 17 Pro Max (iPhone18,2)   physical
""";
        var service = new AppleDeviceDeploymentService(new CapturingProcessRunner(_ => Success(output)));
        var device = Assert.Single(await service.GetDevicesAsync(new AppleDeviceListRequest()));
        Assert.Equal("EvoPhone.coredevice.local", device.Hostname);
        Assert.Equal("00008150-00011C2A3640401C", device.Identifier);
        Assert.Equal("connected", device.State);
        Assert.Equal("iPhone 17 Pro Max (iPhone18,2)", device.Model);
    }
}
