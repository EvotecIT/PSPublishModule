using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationArtifactBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ArtifactBoundary.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationArtifactBoundaryTests() => Directory.CreateDirectory(_root);

    [ArtifactBoundaryCaseSensitiveFact]
    public async Task Case_distinct_sibling_is_not_a_staged_cli_artifact()
    {
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var sibling = Directory.CreateDirectory(Path.Combine(_root, "STAGE")).FullName;
        var payload = Write(sibling, "payload.zip", "payload");
        var metadata = Write(stage, "evidence.json", "{}");
        var manifest = Write(_root, "manifest.json", JsonSerializer.Serialize(new { assetEntries = new object[] {
            new { category = "Tool", target = "app", runtime = "win-x64", style = "Portable", version = "1.2.3", stagedPath = payload },
            new { category = "Metadata", stagedPath = metadata }
        } }));
        var report = await ValidateCliAsync(manifest, stage, [payload, metadata]);
        Assert.False(report.Success);
        Assert.Contains("outside", Assert.Single(report.Errors));
    }

    [Fact]
    public async Task Cancellable_hash_preserves_digest_and_rejects_cancelled_input_before_open()
    {
        var file = Write(_root, "hash-input", new string('x', 200000));
        Assert.Equal(DotNetPublishReleaseArtifactVerifier.ComputeSha256(file),
            await DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(file, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DotNetPublishReleaseArtifactVerifier.ComputeSha256Async(Path.Combine(_root, "missing"), cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_artifact_manifest_is_rejected_before_parsing(bool module)
    {
        var manifest = Path.Combine(_root, "oversized.json");
        using (var file = File.Create(manifest)) file.SetLength(16L * 1024 * 1024 + 1);

        var report = module
            ? await new ReleaseValidationService().RunAsync(new() { Modules = [new() { Path = _root, Manifest = "oversized.json" }] })
            : await ValidateCliAsync(manifest, _root, []);

        Assert.False(report.Success);
        Assert.Contains("byte limit", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bounded_module_manifest_read_preserves_bom_and_legacy_encoding(bool legacy)
    {
        var path = Path.Combine(_root, "Example.psd1");
        const string content = "# café\n@{ ModuleVersion = '1.2.3'; ProcessorArchitecture = 'None' }";
        if (legacy) File.WriteAllBytes(path, System.Text.Encoding.Latin1.GetBytes(content));
        else File.WriteAllText(path, content, System.Text.Encoding.Unicode);
        var report = await new ReleaseValidationService().RunAsync(new() { Modules = [new() {
            Path = _root, Manifest = "Example.psd1", ProcessorArchitecture = "None"
        }] });
        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal("1.2.3", report.Version);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("file")]
    [InlineData("parent")]
    [InlineData("root")]
    [InlineData("metadata")]
    public async Task Staged_cli_evidence_rejects_links_at_every_path_boundary(string variation)
    {
        var physical = Directory.CreateDirectory(Path.Combine(_root, "physical")).FullName;
        var stage = Directory.CreateDirectory(Path.Combine(_root, "stage")).FullName;
        var externalPayload = Write(physical, "payload.zip", "payload");
        var payload = Path.Combine(stage, "payload.zip");
        var metadata = Path.Combine(stage, "evidence.json");
        string? directoryLink = null;
        if (variation == "root") {
            directoryLink = Path.Combine(_root, "stage-link");
            Directory.CreateSymbolicLink(directoryLink, physical);
            stage = directoryLink;
            payload = Path.Combine(stage, "payload.zip");
            metadata = Path.Combine(stage, "evidence.json");
        } else if (variation == "parent") {
            directoryLink = Path.Combine(stage, "nested-link");
            Directory.CreateSymbolicLink(directoryLink, physical);
            payload = Path.Combine(directoryLink, "payload.zip");
        } else if (variation == "file") File.CreateSymbolicLink(payload, externalPayload);
        else File.WriteAllText(payload, "payload");
        if (variation == "metadata") File.CreateSymbolicLink(metadata, Write(physical, "evidence.json", "{}"));
        else File.WriteAllText(metadata, "{}");
        var manifest = Write(_root, "manifest.json", JsonSerializer.Serialize(new { assetEntries = new object[] {
            new { category = "Tool", target = "app", runtime = "win-x64", style = "Portable", version = "1.2.3", stagedPath = payload },
            new { category = "Metadata", stagedPath = metadata }
        } }));
        try {
            var report = await ValidateCliAsync(manifest, stage, [payload, metadata]);

            Assert.Equal(variation == "ordinary", report.Success);
            if (variation == "ordinary") Assert.Equal("CLI app: 1 artifacts", Assert.Single(report.Checks));
            else {
                Assert.Contains("link", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
                Assert.Empty(report.Checks);
            }
            Assert.Equal("payload", File.ReadAllText(externalPayload));
        }
        finally {
            if (variation == "file") File.Delete(payload);
            if (variation == "metadata") File.Delete(metadata);
            if (directoryLink is not null) Directory.Delete(directoryLink);
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Module_zip_preserves_executable_helper_but_drops_privileged_permission_bits()
    {
        var archivePath = Path.Combine(_root, "module.zip");
        using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create)) {
            AddEntry(zip, "Example.psd1", "@{ ModuleVersion = '1.2.3' }", 0x81a4);
            AddEntry(zip, "helper.sh", "#!/bin/sh\nprintf helper-executed\n", 0x8ded); // regular file, 06755
        }
        var evidence = Path.Combine(_root, "helper-observed.json");
        var probe = Write(_root, "probe.ps1", """
            $helper = Join-Path $env:POWERFORGE_MODULE_PATH 'helper.sh'
            if (([int][IO.File]::GetUnixFileMode($helper) -band 64) -eq 0) { throw 'Helper is not executable' }
            $observed = & $helper
            if ($LASTEXITCODE -ne 0) { throw 'Helper execution failed' }
            @{ Output = [string]$observed; Mode = [int][IO.File]::GetUnixFileMode($helper); ModuleRoot = $env:POWERFORGE_MODULE_PATH } |
                ConvertTo-Json -Compress | Set-Content -LiteralPath '__EVIDENCE__'
            """.Replace("__EVIDENCE__", evidence.Replace("'", "''")));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var report = await new ReleaseValidationService().RunAsync(new() { Modules = [new() {
            Path = archivePath, Manifest = "Example.psd1", ProbeScript = probe, Hosts = ["pwsh"]
        }] }, request: new() { ProjectRoot = _root }, cancellationToken: cancellation.Token);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        using var document = JsonDocument.Parse(File.ReadAllText(evidence));
        Assert.Equal("helper-executed", document.RootElement.GetProperty("Output").GetString());
        Assert.Equal(0x1ed, document.RootElement.GetProperty("Mode").GetInt32());
        Assert.False(Directory.Exists(document.RootElement.GetProperty("ModuleRoot").GetString()));
    }

    [Theory]
    [InlineData("1.2.3.0", true)]
    [InlineData("01.2.3", true)]
    [InlineData("1.2.4", false)]
    [InlineData("1.2.3-preview1", false)]
    public async Task Tool_version_comparison_uses_nuget_identity_without_accepting_different_releases(string expectedVersion, bool success)
    {
        using (var zip = ZipFile.Open(Path.Combine(_root, "Example.Tool.1.2.3.nupkg"), ZipArchiveMode.Create))
            AddEntry(zip, "Example.Tool.nuspec", "<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>", 0);
        var calls = new List<ProcessRunRequest>();
        var report = await new ReleaseValidationService(new Runner(request => {
            calls.Add(request);
            Assert.Contains("1.2.3", request.Arguments);
            return new(0, "installed", "", "dotnet", TimeSpan.Zero, false);
        })).RunAsync(new() { Tools = [new() { PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example" }] },
            request: new() { ProjectRoot = _root, Version = expectedVersion });

        Assert.Equal(success, report.Success);
        if (success) { Assert.Single(calls); Assert.Single(report.Checks); Assert.False(Directory.Exists(calls[0].WorkingDirectory)); }
        else { Assert.Empty(calls); Assert.Contains("does not match", Assert.Single(report.Errors)); }
    }

    [Theory]
    [InlineData("1.2.3.0", true)]
    [InlineData("1.2.3-preview1", false)]
    public async Task Cli_artifact_versions_use_the_same_nuget_release_identity(string expectedVersion, bool success)
    {
        var payload = Write(_root, "payload.zip", "payload");
        var metadata = Write(_root, "evidence.json", "{}");
        var manifest = Write(_root, "manifest.json", JsonSerializer.Serialize(new { assetEntries = new object[] {
            new { category = "Tool", target = "app", runtime = "win-x64", style = "Portable", version = "1.2.3", stagedPath = payload },
            new { category = "Metadata", stagedPath = metadata }
        } }));
        var report = await ValidateCliAsync(manifest, _root, [payload, metadata], expectedVersion);
        Assert.Equal(success, report.Success);
        if (!success) Assert.Contains("unexpected version", Assert.Single(report.Errors));
    }

    [Theory]
    [InlineData("previewa", true)]
    [InlineData("previewb", false)]
    public async Task Symbol_package_identity_uses_nuget_prerelease_comparison(string symbolsLabel, bool success)
    {
        foreach (var symbol in new[] { false, true })
        {
            using var zip = ZipFile.Open(Path.Combine(_root, "Example" + (symbol ? ".snupkg" : ".nupkg")), ZipArchiveMode.Create);
            AddEntry(zip, "Example.nuspec", "<package><metadata><id>Example</id><version>1.2.3-" +
                (symbol ? symbolsLabel : "previewA") + "</version><authors>Tests</authors><description>Fixture</description></metadata></package>", 0);
        }
        var report = await new ReleaseValidationService().RunAsync(new() { Packages = new() {
            Path = _root, Items = [new() { Id = "Example", SymbolEntries = ["Example.nuspec"] }]
        } });
        Assert.Equal(success, report.Success);
        if (!success) Assert.Contains("Symbol package identity", Assert.Single(report.Errors));
    }

    private Task<ReleaseValidationReport> ValidateCliAsync(string manifest, string stage, string[] assets, string version = "1.2.3")
        => new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["win-x64"], Styles = ["Portable"], ToolsOnly = true
        } }, request: new() { ProjectRoot = _root, Version = version, StagedAssets = assets, Variables = new() { ["StagingRoot"] = stage } });
    private static void AddEntry(ZipArchive zip, string name, string content, int mode)
    {
        var entry = zip.CreateEntry(name);
        entry.ExternalAttributes = mode << 16;
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
    private static string Write(string root, string name, string content)
    {
        var path = Path.Combine(root, name);
        File.WriteAllText(path, content);
        return path;
    }
    private sealed class Runner(Func<ProcessRunRequest, ProcessRunResult> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => Task.FromResult(execute(request));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal sealed class ArtifactBoundaryUnixFactAttribute : FactAttribute
{
    public ArtifactBoundaryUnixFactAttribute() { if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Skip = "Requires Unix executable permissions."; }
}

internal sealed class ArtifactBoundaryCaseSensitiveFactAttribute : FactAttribute
{
    public ArtifactBoundaryCaseSensitiveFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires the Linux case-sensitive test filesystem."; }
}
