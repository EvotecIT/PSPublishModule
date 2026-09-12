using System.IO.Compression;
using System.Text;

namespace PowerForge.Tests;

public sealed class ReleaseValidationArchiveBoundsTests
{
    [Fact]
    public async Task Module_archive_with_short_extracted_payload_fails_before_runtime_probe()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.ArchiveBounds", Guid.NewGuid().ToString("N"))).FullName;
        try {
            var path = Path.Combine(root, "module.zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) {
                using (var writer = new StreamWriter(zip.CreateEntry("Example.psd1").Open()))
                    writer.Write("@{ ModuleVersion = '1.2.3' }");
                using (var writer = new StreamWriter(zip.CreateEntry("payload.bin").Open()))
                    writer.Write("small payload");
            }
            var bytes = File.ReadAllBytes(path);
            for (var offset = 0; offset <= bytes.Length - 46; offset++) {
                if (BitConverter.ToUInt32(bytes, offset) != 0x02014b50) continue;
                var length = BitConverter.ToUInt16(bytes, offset + 28);
                if (Encoding.UTF8.GetString(bytes, offset + 46, length) == "payload.bin")
                    BitConverter.GetBytes(65536U).CopyTo(bytes, offset + 24);
            }
            File.WriteAllBytes(path, bytes);
            var runner = new UnexpectedProbeRunner();
            var report = await new ReleaseValidationService(runner).RunAsync(new() {
                Modules = [new() { Path = path, Manifest = "Example.psd1", ProbeScript = "must-not-run.ps1" }]
            });

            Assert.False(report.Success);
            Assert.Single(report.Errors);
            Assert.Empty(report.Checks);
            Assert.False(runner.Called);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Bounded_archive_copy_rejects_actual_bytes_past_the_limit()
    {
        using var input = new MemoryStream(new byte[131072]);
        using var output = new MemoryStream();

        Assert.Throws<InvalidDataException>(() => PowerForgeReleaseArtifactVerifier.CopyBounded(input, output, 100000, "Payload"));

        Assert.InRange(output.Length, 1, 100000);
        Assert.True(input.Position > output.Length);
    }

    [Fact]
    public void Bounded_archive_copy_honors_cancellation_before_writing()
    {
        using var input = new MemoryStream(new byte[128]);
        using var output = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            PowerForgeReleaseArtifactVerifier.CopyBounded(input, output, 128, "Payload", cancellation.Token));

        Assert.Equal(0, input.Position);
        Assert.Equal(0, output.Length);
    }

    private sealed class UnexpectedProbeRunner : IProcessRunner
    {
        internal bool Called { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("Runtime probe must not run after a malformed archive.");
        }
    }
}
