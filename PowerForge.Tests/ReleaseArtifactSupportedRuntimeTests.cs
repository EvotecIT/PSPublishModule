using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishReleaseArtifactVerifierTests
{
    [Theory]
    [InlineData("supported", true)]
    [InlineData("wrong-runtime", false)]
    [InlineData("include", true)]
    [InlineData("exclude", false)]
    public void Msi_verification_uses_supported_runtime_fallback_and_matrix_filters(string selection, bool expectedSuccess)
    {
        using var fixture = new ReleaseFixture();
        var config = JsonNode.Parse(File.ReadAllText(fixture.ConfigurationPath))!;
        var target = config["Targets"]![0]!;
        target["SupportedRuntimes"] = new JsonArray(selection == "wrong-runtime" ? "linux-x64" : "win-x64");
        target["Publish"]!.AsObject().Remove("Runtimes");
        if (selection is "include" or "exclude") {
            config["Matrix"] = new JsonObject { [selection == "include" ? "Include" : "Exclude"] =
                new JsonArray(new JsonObject { ["Runtime"] = "win-*" }) };
        }
        fixture.WriteConfiguration(config.ToJsonString());
        fixture.WriteManifest(("Service", "net8.0", "win-x64", "Portable"));

        if (expectedSuccess) {
            var result = fixture.CreateVerifier().Verify(fixture.CreateRequest());
            Assert.Equal("Test.MSI", result.InstallerId);
            Assert.Equal("1.2.3", result.Version);
        } else {
            Assert.Throws<InvalidDataException>(() => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        }
    }
}

public sealed partial class PowerForgeReleaseArtifactVerifierTests
{
    [Theory]
    [InlineData("supported", true)]
    [InlineData("wrong-runtime", false)]
    [InlineData("include", true)]
    [InlineData("exclude", false)]
    public void Portable_verification_uses_supported_runtime_fallback_and_matrix_filters(string selection, bool expectedSuccess)
    {
        using var fixture = new PortableFixture();
        fixture.ConfigureSupportedRuntimeSelection(selection);

        if (expectedSuccess) {
            var result = fixture.CreateVerifier().Verify(fixture.CreateRequest());
            Assert.Equal("Sample.CLI", result.ArtifactId);
            Assert.Equal("1.2.3", result.Version);
        } else {
            Assert.Throws<InvalidDataException>(() => fixture.CreateVerifier().Verify(fixture.CreateRequest()));
        }
    }

    private sealed partial class PortableFixture
    {
        internal void ConfigureSupportedRuntimeSelection(string selection)
        {
            var config = JsonNode.Parse(File.ReadAllText(ConfigurationPath))!;
            var target = config["Targets"]![0]!;
            target["SupportedRuntimes"] = new JsonArray(selection == "wrong-runtime" ? "linux-x64" : "win-x64");
            target["Publish"]!.AsObject().Remove("Runtimes");
            if (selection is "include" or "exclude") {
                config["Matrix"] = new JsonObject { [selection == "include" ? "Include" : "Exclude"] =
                    new JsonArray(new JsonObject { ["Runtime"] = "win-*" }) };
            }
            File.WriteAllText(ConfigurationPath, config.ToJsonString());
            WriteArchive("signed payload");
            WriteDirectInventory();
            WriteBoundCycloneDxSbom("Sample.CLI", "1.2.3", ComputeDigest(ArchivePath));
            WriteChecksums();
        }
    }
}
