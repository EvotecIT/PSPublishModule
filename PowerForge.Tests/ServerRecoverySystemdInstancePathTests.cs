using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class ServerRecoverySecurityTests
{
    [Theory]
    [InlineData("/etc/systemd/system/backup@website.service.d/capture.conf", true)]
    [InlineData("/etc/systemd/system/backup@.service", true)]
    [InlineData("/etc/systemd/../private@website", false)]
    [InlineData("/etc/systemd//backup@website", false)]
    [InlineData("/etc/systemd/backup@website;touch", false)]
    [InlineData("/etc/systemd/backup@website$(id)", false)]
    public void RecoveryPaths_SchemaAndCliAgreeOnSystemdInstancePaths(string path, bool valid)
    {
        var manifest = CreateManifest();
        manifest.Capture!.PlainFiles = [new PowerForgeServerManagedFile { Target = path, Required = true }];
        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);
        Assert.Equal(valid, !errors.Any(error => error.StartsWith("capture.plainFiles[0].target", StringComparison.Ordinal)));

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Schemas", "powerforge.web.serverrecovery.schema.json")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(directory.FullName, "Schemas", "powerforge.web.serverrecovery.schema.json")));
        var document = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["name"] = "instance-path",
            ["target"] = new JsonObject { ["host"] = "example.invalid" },
            ["paths"] = new JsonArray(new JsonObject { ["id"] = "instance", ["path"] = path, ["kind"] = "file" })
        };
        Assert.Equal(valid, schema.Evaluate(document).IsValid);
        var files = new[] { new PowerForgeServerManagedFile { Target = path, Required = true } };
        if (valid)
        {
            Assert.Contains(path, WebCliCommandHandlers.BuildRemoteTarScript(files), StringComparison.Ordinal);
            Assert.Contains(path, WebCliCommandHandlers.BuildRemoteEncryptedCaptureSudoersCommand(files,
                "age18mnmcf7j440ethr6459dvpjy540ll7q2e0088n6gjm4wlmft4cpqhxrmnd"), StringComparison.Ordinal);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => WebCliCommandHandlers.BuildRemoteTarScript(files));
        }
    }
}
