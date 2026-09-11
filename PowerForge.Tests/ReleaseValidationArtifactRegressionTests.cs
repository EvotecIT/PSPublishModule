using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationArtifactRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ArtifactRegression", Guid.NewGuid().ToString("N"));
    public ReleaseValidationArtifactRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("ExePath", "valid", true)]
    [InlineData("OutputDir", "valid", true)]
    [InlineData("ExePath", "missing", false)]
    [InlineData("ExePath", "missing-with-output", false)]
    [InlineData("ExePath", "empty", false)]
    [InlineData("OutputDir", "missing", false)]
    [InlineData("OutputDir", "empty", false)]
    [InlineData("OutputDir", "zero-file", false)]
    [InlineData("none", "valid", false)]
    public async Task Unzipped_manifest_validates_declared_artifacts(string property, string state, bool success)
    {
        var payload = Path.Combine(_root, property == "ExePath" ? "app.exe" : "library");
        if (property == "ExePath" && !state.StartsWith("missing", StringComparison.Ordinal)) File.WriteAllText(payload, state == "empty" ? "" : "payload");
        if (property == "OutputDir" && state != "missing") {
            Directory.CreateDirectory(payload);
            if (state != "empty") File.WriteAllText(Path.Combine(payload, "Library.dll"), state == "zero-file" ? "" : "payload");
        }
        var entry = new Dictionary<string, object> { ["Category"] = "Publish", ["Target"] = "app", ["Runtime"] = "win-x64", ["Style"] = "Portable" };
        if (property != "none") entry[property] = payload;
        if (state == "missing-with-output") {
            var output = Directory.CreateDirectory(Path.Combine(_root, "fallback")).FullName;
            File.WriteAllText(Path.Combine(output, "Library.dll"), "payload");
            entry["OutputDir"] = output;
        }
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new[] { entry }));

        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["win-x64"], Styles = ["Portable"]
        } }, request: new() { ProjectRoot = _root });

        Assert.Equal(success, report.Success);
        if (success) Assert.Equal("CLI app: 1 artifacts", Assert.Single(report.Checks));
        else { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
    }

    [Theory]
    [InlineData("preview1", "1.2.3-preview1", true)]
    [InlineData(null, "1.2.3-preview1", false)]
    [InlineData("preview2", "1.2.3-preview1", false)]
    [InlineData("preview1", "1.2.3", false)]
    [InlineData(null, "1.2.3", true)]
    [InlineData("preview1", null, true)]
    public async Task Module_prerelease_is_part_of_release_identity(string? label, string? requestedVersion, bool success)
    {
        File.WriteAllText(Path.Combine(_root, "Example.psd1"), "@{ ModuleVersion = '1.2.3'; PrivateData = @{ PSData = @{ " +
            (label is null ? "" : "Prerelease = '" + label + "'") + " } } }");

        var report = await new ReleaseValidationService().RunAsync(new() { Modules = [new() { Path = _root, Manifest = "Example.psd1" }] },
            request: new() { ProjectRoot = _root, Version = requestedVersion });

        Assert.Equal(success, report.Success);
        if (success) {
            Assert.Equal(requestedVersion ?? "1.2.3-preview1", report.Version);
            Assert.Contains("Module Example.psd1", report.Checks);
        } else { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
