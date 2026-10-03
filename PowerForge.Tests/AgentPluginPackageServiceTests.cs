using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class AgentPluginPackageServiceTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "powerforge-agent-plugin-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_sandbox, "source");

    public AgentPluginPackageServiceTests()
    {
        Directory.CreateDirectory(Path.Combine(Source, "skills", "summarize", "references"));
        File.WriteAllText(Path.Combine(Source, "plugin.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json","name":"sample-documents","version":"1.2.3","description":"Document tools","extensions":{"com.openai":{"interface":{"displayName":"Documents"}}}}
""");
        File.WriteAllText(Path.Combine(Source, "mcp.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"documents":{"type":"stdio","command":"dotnet","args":["dnx","Sample.Tool@1.2.3","mcp","serve","--stdio"]}}}
""");
        File.WriteAllText(Path.Combine(Source, "skills", "summarize", "SKILL.md"), "---\nname: summarize\ndescription: Summarize documents.\n---\nUse selected results.\n");
        File.WriteAllText(Path.Combine(Source, "skills", "summarize", "references", "limits.md"), "Bounded output.");
    }

    [Fact]
    public void PackProducesInstallableRootAndSeparatePlatformMetadata()
    {
        var result = new AgentPluginPackageService().Pack(Source, Path.Combine(_sandbox, "out"));
        using var archive = ZipFile.OpenRead(result.ArchivePath!);
        Assert.NotNull(archive.GetEntry("plugin.json"));
        Assert.NotNull(archive.GetEntry("mcp.json"));
        Assert.NotNull(archive.GetEntry("skills/summarize/references/limits.md"));
        using var claude = ReadEntry(archive, ".claude-plugin/plugin.json");
        using var codex = ReadEntry(archive, ".codex-plugin/plugin.json");
        Assert.False(claude.RootElement.TryGetProperty("extensions", out _));
        Assert.Equal("Documents", codex.RootElement.GetProperty("interface").GetProperty("displayName").GetString());
        Assert.Equal("./.codex-plugin/mcp.json", codex.RootElement.GetProperty("mcpServers").GetString());
        Assert.Equal(archive.Entries.Count, result.FileCount);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.ArchivePath!))).ToLowerInvariant();
        Assert.Equal(hash, result.Sha256);
        Assert.Equal(hash + "  sample-documents-1.2.3.zip\n", File.ReadAllText(result.ArchivePath + ".sha256"));
        Assert.False(Directory.Exists(Path.Combine(Source, ".claude-plugin")));
    }

    [Fact]
    public void CompatibilityTranslatesHttpAndPluginRootForEachClient()
    {
        File.WriteAllText(Path.Combine(Source, "mcp.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"local":{"type":"stdio","command":"dotnet","args":["${PLUGIN_ROOT}/server.dll"],"env":{"CONFIG":"${PLUGIN_ROOT}/assets/config.json"}},"remote":{"type":"streamable-http","url":"https://example.org/mcp"}}}
""");
        new AgentPluginPackageService().SyncCompatibility(Source);
        using var claude = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, ".mcp.json")));
        using var codex = JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, ".codex-plugin", "mcp.json")));
        Assert.Equal("http", claude.RootElement.GetProperty("mcpServers").GetProperty("remote").GetProperty("type").GetString());
        Assert.Equal("${CLAUDE_PLUGIN_ROOT}/server.dll", claude.RootElement.GetProperty("mcpServers").GetProperty("local").GetProperty("args")[0].GetString());
        Assert.Equal("${PLUGIN_ROOT}/server.dll", codex.RootElement.GetProperty("mcpServers").GetProperty("local").GetProperty("args")[0].GetString());
    }

    [Fact]
    public void ValidateRejectsStaleCompatibilityAndSyncRemovesRetiredMcp()
    {
        var service = new AgentPluginPackageService();
        service.SyncCompatibility(Source);
        File.WriteAllText(Path.Combine(Source, ".claude-plugin", "plugin.json"), "{}");
        Assert.Throws<InvalidDataException>(() => service.Validate(Source));
        service.SyncCompatibility(Source);
        File.Delete(Path.Combine(Source, "mcp.json"));
        Assert.Throws<InvalidDataException>(() => service.Validate(Source));
        service.SyncCompatibility(Source);
        service.Validate(Source);
        Assert.False(File.Exists(Path.Combine(Source, ".mcp.json")));
        Assert.False(File.Exists(Path.Combine(Source, ".codex-plugin", "mcp.json")));
    }

    [Fact]
    public void PackDoesNotOverwriteArchiveOrPackageIntoItsSource()
    {
        var service = new AgentPluginPackageService();
        var result = service.Pack(Source, Path.Combine(_sandbox, "out"));
        var bytes = File.ReadAllBytes(result.ArchivePath!);
        Assert.Throws<IOException>(() => service.Pack(Source, Path.Combine(_sandbox, "out")));
        Assert.Equal(bytes, File.ReadAllBytes(result.ArchivePath!));
        Assert.Throws<InvalidDataException>(() => service.Pack(Source, Path.Combine(Source, "out")));
        Assert.False(Directory.Exists(Path.Combine(Source, "out")));
    }

    [Theory]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"../escape\",\"version\":\"1.2.3\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3.4\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"name\":\"duplicate\",\"version\":\"1.2.3\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3\",\"skills\":\"../external\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3\",\"author\":{\"email\":\"team@example.org\"}}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3\",\"homepage\":\"documentation\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\\n\",\"version\":\"1.2.3\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3\\n\"}")]
    [InlineData("{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/plugin.schema.json\",\"name\":\"sample\",\"version\":\"1.2.3\",\"description\":\"A\",\"extensions\":{\"com.openai\":{\"description\":\"B\"}}}")]
    public void InvalidIdentityCannotProduceAnArtifact(string manifest)
    {
        File.WriteAllText(Path.Combine(Source, "plugin.json"), manifest);
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Pack(Source, Path.Combine(_sandbox, "out")));
        Assert.False(Directory.Exists(Path.Combine(_sandbox, "out")));
    }

    [Theory]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet --some-switch\"}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"env\":{\"PLUGIN_ROOT\":\"/external\"}}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"env\":{\"PATH\":\"a\",\"Path\":\"b\"}}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"env\":{\"plugin_root\":\"/external\"}}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"env\":{\"A=B\":\"value\"}}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"env\":{\"CONFIG\":\"a\\u0000b\"}}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"args\":[\"${PLUGIN_DATA}/state\"]}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"cwd\":\"./\"}")]
    [InlineData("{\"type\":\"streamable-http\",\"url\":\"https://user:password@example.org/mcp\"}")]
    [InlineData("{\"type\":\"stdio\",\"command\":\"dotnet\",\"url\":\"https://example.org/mcp\"}")]
    [InlineData("{\"type\":\"streamable-http\",\"url\":\"http://example.org/mcp\"}")]
    [InlineData("{\"type\":\"sse\",\"url\":\"https://example.org/mcp#fragment\"}")]
    [InlineData("{\"type\":\"streamable-http\",\"url\":\"https://example.org/mcp\",\"headers\":{\"X-Tenant\":\"a\",\"x-tenant\":\"b\"}}")]
    [InlineData("{\"type\":\"streamable-http\",\"url\":\"https://example.org/mcp\",\"headers\":{\"X-Tenant\":\"a\\r\\nb\"}}")]
    [InlineData("{\"type\":\"streamable-http\",\"url\":\"https://example.org/mcp\",\"headers\":{\"X-Tenant\\n\":\"a\"}}")]
    public void UnsupportedMcpProfileIsRejectedRatherThanSilentlyDowngraded(string server)
    {
        File.WriteAllText(Path.Combine(Source, "mcp.json"), "{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json\",\"mcpServers\":{\"server\":" + server + "}}");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Validate(Source));
    }

    [Theory]
    [InlineData("http://localhost:8080/mcp")]
    [InlineData("http://127.0.0.2:8080/mcp")]
    [InlineData("http://[::1]:8080/mcp")]
    public void LoopbackDevelopmentEndpointsRemainUsable(string url)
    {
        File.WriteAllText(Path.Combine(Source, "mcp.json"), "{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json\",\"mcpServers\":{\"server\":{\"type\":\"streamable-http\",\"url\":\"" + url + "\"}}}");
        new AgentPluginPackageService().Validate(Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("server\n")]
    public void InvalidServerIdentifierCannotProduceClientConfiguration(string name)
    {
        File.WriteAllText(Path.Combine(Source, "mcp.json"), "{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json\",\"mcpServers\":{" + JsonSerializer.Serialize(name) + ":{\"type\":\"stdio\",\"command\":\"dotnet\"}}}");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().SyncCompatibility(Source));
        Assert.False(Directory.Exists(Path.Combine(Source, ".codex-plugin")));
    }

    [Fact]
    public void WindowsRelativeCommandCannotProduceAUnixIncompatibleLauncher()
    {
        Directory.CreateDirectory(Path.Combine(Source, "scripts"));
        File.WriteAllText(Path.Combine(Source, "scripts", "server.exe"), "server");
        File.WriteAllText(Path.Combine(Source, "mcp.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"server":{"type":"stdio","command":"./scripts\\server.exe"}}}
""");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Validate(Source));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnicodeEquivalentResourcePathsCannotOverwriteOnAnotherFilesystem(bool directories)
    {
        var assets = Path.Combine(Source, "assets");
        Directory.CreateDirectory(assets);
        var composed = Path.Combine(assets, "caf\u00e9");
        var decomposed = Path.Combine(assets, "cafe\u0301");
        if (directories) Directory.CreateDirectory(composed);
        else File.WriteAllText(composed, "first");
        if (File.Exists(decomposed) || Directory.Exists(decomposed)) return; // Normalization-insensitive filesystem already aliases the names.
        if (directories) Directory.CreateDirectory(decomposed);
        else File.WriteAllText(decomposed, "second");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Pack(Source, Path.Combine(_sandbox, "out")));
        Assert.False(Directory.Exists(Path.Combine(_sandbox, "out")));
    }

    [Fact]
    public void UniqueUnicodeResourcesRetainTheirArchivedNames()
    {
        Directory.CreateDirectory(Path.Combine(Source, "assets"));
        File.WriteAllText(Path.Combine(Source, "assets", "caf\u00e9.json"), "resource");
        var sourceName = Path.GetFileName(Directory.GetFiles(Path.Combine(Source, "assets")).Single());
        var result = new AgentPluginPackageService().Pack(Source, Path.Combine(_sandbox, "out"));
        using var archive = ZipFile.OpenRead(result.ArchivePath!);
        using var reader = new StreamReader(archive.GetEntry("assets/" + sourceName)!.Open());
        Assert.Equal("resource", reader.ReadToEnd());
    }

    [Fact]
    public void CaseVariantSiblingCannotSupplyAnExecutableOnCaseSensitiveFilesystems()
    {
        var sibling = Path.Combine(_sandbox, "SOURCE");
        if (Directory.Exists(sibling)) return; // A case-insensitive filesystem aliases the permitted root.
        Directory.CreateDirectory(sibling);
        File.WriteAllText(Path.Combine(sibling, "external"), "external executable");
        File.WriteAllText(Path.Combine(Source, "mcp.json"), """
{"$schema":"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json","mcpServers":{"external":{"type":"stdio","command":"./../SOURCE/external"}}}
""");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Validate(Source));
    }

    [Fact]
    public void PackageLinksCannotIncludeExternalFilesOrWriteExternalOutputs()
    {
        if (OperatingSystem.IsWindows()) return; // Windows link creation requires machine-specific privileges.
        var external = Path.Combine(_sandbox, "external");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "private.txt"), "private");
        var link = Path.Combine(Source, "assets");
        Directory.CreateSymbolicLink(link, external);
        var service = new AgentPluginPackageService();
        Assert.Throws<InvalidDataException>(() => service.Validate(Source));
        Directory.Delete(link);
        var outputLink = Path.Combine(_sandbox, "out-link");
        Directory.CreateSymbolicLink(outputLink, external);
        Assert.Throws<InvalidDataException>(() => service.Pack(Source, outputLink));
        Assert.Single(Directory.GetFiles(external));
    }

    [Fact]
    public void PortableArchivePreservesExecutableScriptPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.Combine(Source, "scripts"));
        var script = Path.Combine(Source, "scripts", "server.sh");
        File.WriteAllText(script, "#!/bin/sh\nexit 0\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var result = new AgentPluginPackageService().Pack(Source, Path.Combine(_sandbox, "out"));
        using var archive = ZipFile.OpenRead(result.ArchivePath!);
        Assert.Equal(0x1ED, (archive.GetEntry("scripts/server.sh")!.ExternalAttributes >> 16) & 0x1FF);
    }

    [Theory]
    [InlineData(".env")]
    [InlineData("credentials.json")]
    [InlineData("skills/summarize/references/NUL.txt")]
    public void UnexpectedOrNonPortableFilesAreRejected(string path)
    {
        var absolute = Path.Combine(Source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        if (OperatingSystem.IsWindows() && path.EndsWith("NUL.txt", StringComparison.Ordinal)) return;
        File.WriteAllText(absolute, "not a package resource");
        Assert.Throws<InvalidDataException>(() => new AgentPluginPackageService().Validate(Source));
    }

    private static JsonDocument ReadEntry(ZipArchive archive, string path)
    {
        using var stream = archive.GetEntry(path)!.Open();
        return JsonDocument.Parse(stream);
    }

    public void Dispose() => Directory.Delete(_sandbox, recursive: true);
}
