using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class ServerRecoveryDurableBackupTests
{
    [Fact]
    public void Validation_ShouldAcceptACompleteDurableBackupContract()
    {
        var manifest = CreateManifest();

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validation_ShouldRejectUnsafeOrIncompleteDurableBackupContracts()
    {
        var manifest = CreateManifest();
        manifest.DurableBackup!.ExportRoot = "/var/lib";
        manifest.DurableBackup.StagingRetentionHours = 12;
        manifest.DurableBackup.Recipient = "not-an-age-recipient";
        manifest.DurableBackup.Databases![0].Database = "example;drop";
        manifest.DurableBackup.ArtifactStores![0].Path = "/var/lib/powerforge-backup-export/artifacts";
        manifest.DurableBackup.EncryptedFiles![0].Target = "/var/lib/powerforge-backup-export/artifacts/secret.env";

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("dedicated directory", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("24 through 720", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("literal age public recipient", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("database name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must not overlap", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("must not overlap artifact store", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_ShouldMatchRuntimeIdentityAndRecipientBoundaries()
    {
        var manifest = CreateManifest();
        manifest.DurableBackup!.ExportGroup = new string('g', 33);
        manifest.DurableBackup.Recipient = "age1";

        var errors = WebCliCommandHandlers.ValidateServerRecoveryManifest(manifest);

        Assert.Contains(errors, error => error.Contains("valid Linux group name", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("literal age public recipient", StringComparison.Ordinal));
    }

    [Fact]
    public void Schema_ShouldAcceptTheTypedContractAndRejectUnknownFields()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetRepoPath("Schemas", "powerforge.web.serverrecovery.schema.json")));
        var valid = JsonNode.Parse("""
            {
              "schemaVersion": 2,
              "name": "example",
              "target": { "host": "example.invalid" },
              "durableBackup": {
                "exportRoot": "/var/lib/powerforge-backup-export",
                "exportGroup": "powerforge-export",
                "recipient": "age1example",
                "stagingRetentionHours": 48,
                "databases": [
                  { "id": "control", "provider": "postgresql", "database": "control", "required": true }
                ],
                "encryptedFiles": [
                  { "target": "/etc/example.env", "required": true, "sensitive": true }
                ],
                "artifactStores": [
                  { "id": "releases", "path": "/var/lib/example-releases", "required": true }
                ]
              }
            }
            """)!;

        Assert.True(schema.Evaluate(valid, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        var missingRecipient = valid.DeepClone();
        missingRecipient["durableBackup"]!.AsObject().Remove("recipient");
        Assert.False(schema.Evaluate(missingRecipient, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        var broadExportRoot = valid.DeepClone();
        broadExportRoot["durableBackup"]!["exportRoot"] = "/var/lib";
        Assert.False(schema.Evaluate(broadExportRoot, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
        valid["durableBackup"]!["unknown"] = true;
        Assert.False(schema.Evaluate(valid, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void LinuxCapture_ShouldEncryptValidatedDatabaseDumpsAndPublishAtomically()
    {
        var script = File.ReadAllText(GetRepoPath("Deployment", "Linux", "powerforge-server-data-capture.sh"));

        Assert.Contains("pg_dump", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore", script, StringComparison.Ordinal);
        Assert.Contains("pg_dumpall", script, StringComparison.Ordinal);
        Assert.Contains("age_bin", script, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS", script, StringComparison.Ordinal);
        Assert.Contains("mv -T -- \"$partial\" \"$final\"", script, StringComparison.Ordinal);
        Assert.Contains("--link-dest", script, StringComparison.Ordinal);
        Assert.Contains("manifest must be owned by root", script, StringComparison.Ordinal);
        Assert.Contains("manifest must not be group- or world-writable", script, StringComparison.Ordinal);
        Assert.Contains("encrypted path overlaps plaintext artifact store", script, StringComparison.Ordinal);
        Assert.Contains("artifact store overlaps durable export root", script, StringComparison.Ordinal);
        Assert.Contains("export root must be a dedicated directory", script, StringComparison.Ordinal);
        Assert.Contains("artifact store contains a link or special entry", script, StringComparison.Ordinal);
        Assert.Contains("export root must be a canonical non-link directory", script, StringComparison.Ordinal);
        Assert.DoesNotContain("eval ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("source \"$manifest\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PullRuntime_ShouldPinSshVerifyBeforeRetentionAndProtectItsRoot()
    {
        var script = File.ReadAllText(GetRepoPath("Deployment", "Linux", "powerforge-server-backup-pull.sh"));

        Assert.Contains("StrictHostKeyChecking=yes", script, StringComparison.Ordinal);
        Assert.Contains("IdentitiesOnly=yes", script, StringComparison.Ordinal);
        Assert.Contains("BatchMode=yes", script, StringComparison.Ordinal);
        Assert.Contains("sha256_bin\" -c SHA256SUMS", script, StringComparison.Ordinal);
        Assert.Contains("age-encryption.org/v1", script, StringComparison.Ordinal);
        Assert.Contains(".powerforge-server-backup-root", script, StringComparison.Ordinal);
        Assert.Contains("verify_snapshot \"$snapshot\"", script, StringComparison.Ordinal);
        Assert.Contains("has_current_verification \"$snapshot\"", script, StringComparison.Ordinal);
        Assert.Contains("snapshot contains a link or special entry", script, StringComparison.Ordinal);
        Assert.Contains("destination must be a canonical physical path", script, StringComparison.Ordinal);
        Assert.Contains("flock_bin", script, StringComparison.Ordinal);
        Assert.Contains("[[ \"$remote_root\" == '/' ||", script, StringComparison.Ordinal);
        Assert.Contains("return 0\n}\ntrap cleanup EXIT INT TERM", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--delete", script, StringComparison.Ordinal);
        Assert.DoesNotContain("StrictHostKeyChecking=no", script, StringComparison.Ordinal);
    }

    private static PowerForgeServerRecoveryManifest CreateManifest()
        => new()
        {
            SchemaVersion = 2,
            Name = "example",
            Target = new PowerForgeServerTarget { Host = "example.invalid" },
            DurableBackup = new PowerForgeServerDurableBackup
            {
                ExportRoot = "/var/lib/powerforge-backup-export",
                ExportGroup = "powerforge-export",
                Recipient = "age1example",
                StagingRetentionHours = 48,
                Databases =
                [
                    new PowerForgeServerDurableBackupDatabase
                    {
                        Id = "control",
                        Provider = "postgresql",
                        Database = "control",
                        Required = true
                    }
                ],
                EncryptedFiles =
                [
                    new PowerForgeServerManagedFile
                    {
                        Target = "/etc/example.env",
                        Required = true,
                        Sensitive = true
                    }
                ],
                ArtifactStores =
                [
                    new PowerForgeServerDurableBackupArtifactStore
                    {
                        Id = "releases",
                        Path = "/var/lib/example-releases",
                        Required = true
                    }
                ]
            }
        };

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }
}
