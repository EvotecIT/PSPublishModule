using System.Text.Json;
using System.Security.Cryptography;

namespace PowerForge.Tests;

public sealed class SigningPreparationServiceTests
{
    private const string Thumbprint = "0123456789ABCDEF0123456789ABCDEF01234567";

    [Fact]
    public void Prepare_RetainsOneGeneratedProofAndExactIdentityReceipt()
    {
        var directory = NewDirectory();
        var runner = new Runner(request => {
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(request.WorkingDirectory!, "signing-proof.ps1")))).ToLowerInvariant();
            return new PowerShellRunResult(0, JsonSerializer.Serialize(new { signatureStatus = "Valid", thumbprint = Thumbprint, artifactSha256 = digest }), "", "powershell");
        });
        try
        {
            var before = DateTimeOffset.UtcNow;
            var result = new SigningPreparationService(runner).Prepare(new() { Thumbprint = Thumbprint, OutputDirectory = directory });
            Assert.Equal(1, runner.Calls);
            Assert.Equal(Thumbprint, result.Thumbprint);
            Assert.Equal("Valid", result.SignatureStatus);
            Assert.InRange(result.VerifiedAtUtc, before, DateTimeOffset.UtcNow);
            var expectedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(result.ArtifactPath))).ToLowerInvariant();
            Assert.Equal(expectedDigest, result.ArtifactSha256);
            Assert.True(File.Exists(result.ArtifactPath));
            using var receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
            Assert.Equal(Thumbprint, receipt.RootElement.GetProperty("thumbprint").GetString());
            Assert.Equal(expectedDigest, receipt.RootElement.GetProperty("artifactSha256").GetString());
            Assert.Equal("provider-managed; expiry unknown", receipt.RootElement.GetProperty("cacheScope").GetString());
            Assert.False(File.Exists(Path.Combine(directory, "prepare.ps1")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1, "", "denied")]
    [InlineData(0, "{}", "")]
    [InlineData(0, "{\"signatureStatus\":\"NotSigned\",\"thumbprint\":\"0123456789ABCDEF0123456789ABCDEF01234567\"}", "")]
    [InlineData(0, "{\"signatureStatus\":\"Valid\",\"thumbprint\":\"FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF\"}", "")]
    public void Prepare_FailedOrUnverifiedAttemptNeverWritesSuccessOrRetries(int exitCode, string output, string error)
    {
        var directory = NewDirectory();
        var runner = new Runner(_ => new PowerShellRunResult(exitCode, output, error, "powershell"));
        try
        {
            var exception = Record.Exception(() => new SigningPreparationService(runner).Prepare(new() { Thumbprint = Thumbprint, OutputDirectory = directory }));
            Assert.NotNull(exception);
            Assert.Equal(1, runner.Calls);
            Assert.False(File.Exists(Path.Combine(directory, "receipt.json")));
            Assert.False(File.Exists(Path.Combine(directory, "prepare.ps1")));
            Assert.False(Directory.Exists(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Prepare_RejectsChangedProofBytesWithoutCreatingSuccessReceipt()
    {
        var directory = NewDirectory();
        var runner = new Runner(request => {
            var artifact = Path.Combine(request.WorkingDirectory!, "signing-proof.ps1");
            var verifiedDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artifact))).ToLowerInvariant();
            File.WriteAllText(artifact, "replacement bytes after verification");
            return new PowerShellRunResult(0, JsonSerializer.Serialize(new { signatureStatus = "Valid", thumbprint = Thumbprint, artifactSha256 = verifiedDigest }), "", "powershell");
        });
        Assert.Throws<InvalidOperationException>(() => new SigningPreparationService(runner).Prepare(new() { Thumbprint = Thumbprint, OutputDirectory = directory }));
        Assert.Equal(1, runner.Calls);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Prepare_ResolvesExplicitModuleBeforeChangingWorkingDirectory()
    {
        var directory = NewDirectory();
        var module = Path.Combine("local-module", "PSPublishModule.psd1");
        var expected = Path.GetFullPath(module);
        var runner = new Runner(request => {
            var index = request.Arguments.ToList().IndexOf("-ModulePath");
            Assert.Equal(expected, request.Arguments[index + 1]);
            Assert.NotEqual(directory, request.WorkingDirectory);
            Assert.Equal(TimeSpan.FromMinutes(5), request.Timeout);
            Assert.Equal(PowerShellHostRequirement.WindowsPowerShell, request.HostRequirement);
            throw new IOException("synthetic provider interruption");
        });
        Assert.Throws<IOException>(() => new SigningPreparationService(runner).Prepare(new() { Thumbprint = Thumbprint, OutputDirectory = directory, ModulePath = module }));
        Assert.Equal(1, runner.Calls);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Prepare_PreservesExistingDestinationBeforeRequestingSigner()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        var sentinel = Path.Combine(directory, "keep.txt");
        File.WriteAllText(sentinel, "unchanged");
        var runner = new Runner(_ => throw new InvalidOperationException("Must not invoke signer."));
        try
        {
            Assert.Throws<IOException>(() => new SigningPreparationService(runner).Prepare(new() { Thumbprint = Thumbprint, OutputDirectory = directory }));
            Assert.Equal(0, runner.Calls);
            Assert.Equal("unchanged", File.ReadAllText(sentinel));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string NewDirectory() => Path.Combine(AppleReleaseArtifactService.ResolvePhysicalPath(Path.GetTempPath()), "powerforge-signing-preparation-" + Guid.NewGuid().ToString("N"));

    private sealed class Runner(Func<PowerShellRunRequest, PowerShellRunResult> execute) : IPowerShellRunner
    {
        public int Calls { get; private set; }
        public PowerShellRunResult Run(PowerShellRunRequest request) { Calls++; return execute(request); }
    }
}
