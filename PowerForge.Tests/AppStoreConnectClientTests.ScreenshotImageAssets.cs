using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ScreenshotRead_ExposesDeliveredPixelsForVisualInspection()
    {
        var handler = new RecordingHandler("""{"data":[{"type":"appScreenshots","id":"image-1","attributes":{"fileName":"home.png","assetDeliveryState":{"state":"COMPLETE"},"imageAsset":{"templateUrl":"https://example.test/image/{w}x{h}.{f}","width":1320,"height":2868}}}]}""");
        using var http = new HttpClient(handler);
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var image = Assert.Single(await client.GetScreenshotsAsync("set-1"));
        Assert.Equal("COMPLETE", image.AssetDeliveryState);
        Assert.Equal("https://example.test/image/{w}x{h}.{f}", image.ImageAssetUrlTemplate);
        Assert.Equal(1320, image.ImageAssetWidth);
        Assert.Equal(2868, image.ImageAssetHeight);
    }
}
