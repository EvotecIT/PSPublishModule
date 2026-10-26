using PowerForge.Web;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class AppleStoreArtTests
{
    [Fact]
    public void ReleasedCompositionSignatureRemainsAvailableToCompiledCallers()
    {
        Assert.NotNull(typeof(AppleStoreArtComposer).GetMethod(nameof(AppleStoreArtComposer.ComposeAsync),
            [typeof(string), typeof(string), typeof(string), typeof(CancellationToken)]));
    }

    [Fact]
    public async Task RetainedOutputVerificationRejectsChangedCaptionsRawPixelsAndExtraFrames()
    {
        WritePng("watch.png", 416, 496);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"devices":["watch"],"frames":[{"id":"home","layout":"watch","capture":"watch.png"}]}""");
        var captions = Path.Combine(root, "store-captions.en-US.json");
        File.WriteAllText(captions, "{}");
        var output = Path.Combine(root, "output");
        var composer = new AppleStoreArtComposer();
        await composer.ComposeAsync(config, output);
        Assert.Single(composer.Verify(config, output));
        var lineage = Path.Combine(output, "store-art.lineage.json");
        var retained = File.ReadAllText(lineage);
        var changed = System.Text.Json.Nodes.JsonNode.Parse(retained)!;
        changed[0]!["renderSha256"] = new string('a', 64);
        File.WriteAllText(lineage, changed.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => composer.Verify(config, output));
        File.WriteAllText(lineage, retained);
        File.WriteAllText(captions, "{\"title\":\"Changed\"}");
        Assert.Throws<InvalidOperationException>(() => composer.Verify(config, output));
        File.WriteAllText(captions, "{}");
        WritePng("watch.png", 416, 496, ImageMagick.MagickColors.Red);
        Assert.Throws<InvalidOperationException>(() => composer.Verify(config, output));
        WritePng("watch.png", 416, 496);
        File.Copy(Path.Combine(root, "watch.png"), Path.Combine(output, "en-US", "watch", "unlisted.png"));
        Assert.Throws<InvalidOperationException>(() => composer.Verify(config, output));
    }

    [Fact]
    public async Task LocalSubsetProofDoesNotRequireUnselectedLocaleCaptures()
    {
        WritePng("watch.png", 416, 496);
        var config = Path.Combine(root, "store-art.json");
        File.WriteAllText(config, """{"locales":["en-US","pl"],"devices":["watch","iphone-69"],"frames":[{"id":"home","layout":"watch","capture":"watch.png","devices":["watch"]},{"id":"phone","capture":"missing.png","devices":["iphone-69"]}]}""");
        File.WriteAllText(Path.Combine(root, "store-captions.en-US.json"), "{}");
        var composer = new AppleStoreArtComposer();
        var output = Path.Combine(root, "output");
        await composer.ComposeAsync(config, output, null, default, locales: ["en-US"], devices: ["watch"]);
        Assert.Single(composer.Verify(config, output, ["en-US"], ["watch"]));
        await Assert.ThrowsAsync<ArgumentException>(() => composer.ComposeAsync(config, Path.Combine(root, "invalid"), null, default, ["ja"], null));
    }
}
