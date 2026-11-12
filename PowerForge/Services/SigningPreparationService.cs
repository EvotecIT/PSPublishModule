using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Signs and verifies one generated local proof through the existing signing provider.</summary>
public sealed class SigningPreparationService
{
    private readonly IPowerShellRunner _runner;

    /// <summary>Creates a preparation service using the normal bounded PowerShell host.</summary>
    public SigningPreparationService() : this(new PowerShellRunner()) { }

    internal SigningPreparationService(IPowerShellRunner runner)
        => _runner = runner ?? throw new ArgumentNullException(nameof(runner));

    /// <summary>
    /// Requests one signature. Native provider PIN/touch prompts remain interactive;
    /// no PIN is accepted or cached here and no retry or release is performed.
    /// </summary>
    public SigningPreparationResult Prepare(SigningPreparationRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var thumbprint = (request.Thumbprint ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
        if (thumbprint.Length != 40 || thumbprint.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("An exact 40-character approved certificate thumbprint is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new ArgumentException("A new local output directory is required.", nameof(request));
        if (!Uri.TryCreate(request.TimestampUrl, UriKind.Absolute, out var timestamp) ||
            (timestamp.Scheme != Uri.UriSchemeHttps && timestamp.Scheme != Uri.UriSchemeHttp) ||
            !string.IsNullOrEmpty(timestamp.UserInfo))
            throw new ArgumentException("The timestamp service must be a credential-free HTTP(S) URL.", nameof(request));
        var directory = Path.GetFullPath(request.OutputDirectory);
        RequireLocalStorage(directory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Signing preparation requires a new output directory; existing content is preserved.");
        for (var ancestor = Directory.GetParent(directory); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Signing preparation does not write through linked directories.");
            if (Directory.Exists(Path.Combine(ancestor.FullName, ".git")) || File.Exists(Path.Combine(ancestor.FullName, ".git")))
                throw new IOException("Keep signing preparation receipts outside a source checkout.");
        }
        var module = string.IsNullOrWhiteSpace(request.ModulePath) ? "PSPublishModule" : Path.GetFullPath(request.ModulePath!);
        var operation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PowerForge", "Signing", "operations", Guid.NewGuid().ToString("N"));
        RequireLocalStorage(operation);
        for (var ancestor = Directory.GetParent(operation); ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The signing operation directory must not traverse links.");
        Directory.CreateDirectory(operation);
        var artifact = Path.Combine(operation, "signing-proof.ps1");
        var script = Path.Combine(operation, "prepare.ps1");
        using (var output = new FileStream(artifact, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(output, new UTF8Encoding(true)))
            writer.WriteLine("# Local signing-provider preparation proof; no release authority.\r\n$null = '" + Guid.NewGuid().ToString("D") + "'");
        try
        {
            using (var output = new FileStream(script, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(true)))
                writer.Write(EmbeddedScripts.Load("Scripts/Signing/Prepare-SigningSession.ps1"));
            PowerShellRunResult execution;
            using (var scriptLock = new FileStream(script, FileMode.Open, FileAccess.Read, FileShare.Read))
                execution = _runner.Run(new PowerShellRunRequest(script,
                    new[] { "-Directory", operation, "-Thumbprint", thumbprint,
                        "-TimestampUrl", timestamp.AbsoluteUri, "-ModulePath", module },
                    TimeSpan.FromMinutes(5), preferPwsh: false,
                    hostRequirement: PowerShellHostRequirement.WindowsPowerShell, workingDirectory: operation));
            if (execution.ExitCode != 0)
                throw new InvalidOperationException("Signing preparation failed; no provider-readiness claim was recorded. " + execution.StdErr);
            using var json = JsonDocument.Parse(execution.StdOut);
            var root = json.RootElement;
            var status = root.GetProperty("signatureStatus").GetString();
            var signer = root.GetProperty("thumbprint").GetString();
            if (!string.Equals(status, "Valid", StringComparison.Ordinal) ||
                !string.Equals(signer, thumbprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The generated proof did not verify with the expected signing certificate.");
            using var sha = SHA256.Create();
            using var bytes = new FileStream(artifact, FileMode.Open, FileAccess.Read, FileShare.Read);
            var digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            if (!string.Equals(digest, root.GetProperty("artifactSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The signed proof bytes changed after signature verification.");
            if (Directory.Exists(directory) || File.Exists(directory))
                throw new IOException("The output destination appeared during preparation; it was left unchanged.");
            Directory.CreateDirectory(directory);
            var retainedArtifact = Path.Combine(directory, "signing-proof.ps1");
            bytes.Position = 0;
            using (var output = new FileStream(retainedArtifact, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                bytes.CopyTo(output);
            var result = new SigningPreparationResult {
                VerifiedAtUtc = DateTimeOffset.UtcNow,
                Thumbprint = thumbprint,
                ArtifactPath = retainedArtifact,
                ArtifactSha256 = digest,
                SignatureStatus = status!
            };
            using (var output = new FileStream(Path.Combine(directory, "receipt.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false)))
                writer.Write(JsonSerializer.Serialize(result, new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
                }));
            return result;
        }
        finally
        {
            File.Delete(script);
            File.Delete(artifact);
            Directory.Delete(operation);
        }
    }

    private static void RequireLocalStorage(string path)
    {
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Ram))
            throw new IOException("Signing preparation requires a local fixed disk, not a network or removable destination.");
    }
}
