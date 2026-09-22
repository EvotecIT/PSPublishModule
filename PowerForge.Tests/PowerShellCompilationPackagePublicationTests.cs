using System.Security.Cryptography;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationPackagePublicationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Publication_CancellationDuringOrAfterWritingPreservesDestination(bool existing, bool cancelAfterWriting)
    {
        using var fixture = new PublicationFixture(existing);
        using var cancellation = new CancellationTokenSource();
        var error = Assert.ThrowsAny<OperationCanceledException>(() =>
            PowerShellCompilationLibraryPackageBuilder.PublishPackage(fixture.Output, target =>
            {
                if (cancelAfterWriting)
                {
                    target.WriteByte(42);
                    cancellation.Cancel();
                }
                else
                {
                    using var source = new CancelingReadStream(cancellation);
                    PowerShellCompilationLibraryPackageBuilder.CopyStream(source, target, cancellation.Token);
                }
            }, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        fixture.AssertUnchanged(existing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publication_WriterFailurePreservesDestinationAndOriginalError(bool existing)
    {
        using var fixture = new PublicationFixture(existing);
        var expected = new IOException("Archive input failed.");
        var actual = Assert.Throws<IOException>(() =>
            PowerShellCompilationLibraryPackageBuilder.PublishPackage(fixture.Output, target =>
            {
                target.WriteByte(42);
                throw expected;
            }, default));

        Assert.Same(expected, actual);
        fixture.AssertUnchanged(existing);
    }

    [Fact]
    public void Publication_CleanupFailureDoesNotHideWriterFailure()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new PublicationFixture(existing: true);
        var expected = new IOException("Archive input failed.");
        string? temporary = null;
        try
        {
            var actual = Assert.Throws<IOException>(() =>
                PowerShellCompilationLibraryPackageBuilder.PublishPackage(fixture.Output, target =>
                {
                    temporary = ((FileStream)target).Name;
                    File.SetAttributes(temporary, FileAttributes.ReadOnly);
                    throw expected;
                }, default));
            Assert.Same(expected, actual);
            Assert.Contains(actual.Data.Keys.Cast<string>(), key => key.StartsWith("PowerForge.PackageCleanupFailure.", StringComparison.Ordinal));
            Assert.Equal("previous package", File.ReadAllText(fixture.Output));
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.SetAttributes(temporary, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Publication_LockedWindowsDestinationIsNotReplaced()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new PublicationFixture(existing: true);
        using (var locked = new FileStream(fixture.Output, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => PowerShellCompilationLibraryPackageBuilder.PublishPackage(
                fixture.Output, target => target.WriteByte(42), default));
        fixture.AssertUnchanged(existing: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publication_CommitsCompleteBytesAndTheirHash(bool existing)
    {
        using var fixture = new PublicationFixture(existing);
        var content = new byte[] { 1, 2, 3, 4 };
        var hash = PowerShellCompilationLibraryPackageBuilder.PublishPackage(fixture.Output,
            target => target.Write(content, 0, content.Length), default);

        Assert.Equal(content, File.ReadAllBytes(fixture.Output));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), hash);
        Assert.Equal(new[] { fixture.Output }, Directory.GetFiles(fixture.Root));
    }

    private sealed class CancelingReadStream(CancellationTokenSource cancellation) : MemoryStream(new byte[163840])
    {
        private int _reads;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            if (++_reads == 2) cancellation.Cancel();
            return read;
        }
    }

    private sealed class PublicationFixture : IDisposable
    {
        internal PublicationFixture(bool existing)
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powerforge-package-publication-" + Guid.NewGuid().ToString("N"))).FullName;
            Output = Path.Combine(Root, "library.nupkg");
            if (existing) File.WriteAllText(Output, "previous package");
        }

        internal string Root { get; }
        internal string Output { get; }

        internal void AssertUnchanged(bool existing)
        {
            if (existing) Assert.Equal("previous package", File.ReadAllText(Output));
            else Assert.False(File.Exists(Output));
            Assert.Equal(existing ? new[] { Output } : Array.Empty<string>(), Directory.GetFiles(Root));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
