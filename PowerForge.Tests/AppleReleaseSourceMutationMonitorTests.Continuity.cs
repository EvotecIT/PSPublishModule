using System.Reflection;
using System.Runtime.InteropServices;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseSourceMutationMonitorTests
{
    [Theory]
    [InlineData(0x3u)]
    [InlineData(0x5u)]
    [InlineData(0x7u)]
    public void Producer_buffer_loss_requires_fresh_identity_but_does_not_poison_later_consumption(uint flags)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.NativeError(flags);

        var identity = fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer");

        Assert.Equal(fixture.Capture(), identity);
        fixture.Monitor.ValidateNoChanges();
    }

    [Fact]
    public void Buffer_loss_during_first_identity_capture_rejects_output()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Monitor.CaptureExpectedProducerOutput(() =>
            {
                var identity = fixture.Capture();
                fixture.NativeError(0x3);
                return identity;
            }, "test producer"));

        Assert.Contains("native event continuity", exception.Message, StringComparison.Ordinal);
        Assert.Contains("being bound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Buffer_loss_during_armed_identity_comparison_rejects_output()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        var captures = 0;
        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Monitor.CaptureExpectedProducerOutput(() =>
            {
                if (++captures == 2)
                    fixture.NativeError(0x3);
                return fixture.Capture();
            }, "test producer"));

        Assert.Contains("lost continuity", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Buffer_loss_after_binding_remains_terminal()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer");
        fixture.NativeError(0x3);

        var exception = Assert.Throws<InvalidOperationException>(() => fixture.Monitor.ValidateNoChanges());

        Assert.Contains("lost continuity", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x20u)] // Root changed.
    [InlineData(0x8u)] // Event identifiers wrapped.
    [InlineData(0x20003u)] // Unknown mixed flags cannot acquire producer-only recovery semantics.
    public void Non_buffer_observer_failure_before_binding_remains_terminal(uint flags)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.NativeError(flags);

        Assert.Throws<InvalidOperationException>(() =>
            fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer"));
    }

    [Fact]
    public void Producer_buffer_loss_cannot_hide_a_later_unrelated_observer_error()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.NativeError(0x3);
        fixture.Error(new IOException("unreadable native event path"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer"));

        Assert.Contains("unreadable native event path", exception.Message, StringComparison.Ordinal);
    }

    private sealed class NativeContinuityFixture : IDisposable
    {
        private readonly DirectoryInfo _root;
        private readonly string _artifact;
        internal AppleReleaseSourceMutationMonitor Monitor { get; }
        internal Action<Exception> Error { get; }
        internal Action<uint> NativeError { get; }

        internal NativeContinuityFixture()
        {
            _root = Directory.CreateDirectory(Path.Combine(
                AppleReleaseArtifactService.ResolvePhysicalPath(Path.GetTempPath()),
                "PowerForge.NativeContinuityTests", Guid.NewGuid().ToString("N")));
            _artifact = Path.Combine(_root.FullName, "producer.bin");
            File.WriteAllText(_artifact, "producer output");
            Monitor = new AppleReleaseSourceMutationMonitor(
                _root.FullName, enableImmediately: false, unchangedMacOSCloneSource: _ => false);

            // Inject at the existing native observer error boundary. No production test hook,
            // OS-global event flood, callback timing assumption or observer replacement is needed.
            const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
            var observer = typeof(AppleReleaseSourceMutationMonitor).GetField("_observer", access)!.GetValue(Monitor)!;
            Error = (Action<Exception>)observer.GetType().GetField("_error", access)!.GetValue(observer)!;
            var self = (GCHandle)observer.GetType().GetField("_self", access)!.GetValue(observer)!;
            var receive = observer.GetType().GetMethod("ReceiveEvents", BindingFlags.Static | BindingFlags.NonPublic)!;
            NativeError = flags =>
            {
                // Continuity flags are processed before event paths. Supply the actual native
                // flag word so this also covers classification by the native callback itself.
                var word = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteInt32(word, unchecked((int)flags));
                    receive.Invoke(null, new object[]
                    {
                        IntPtr.Zero, GCHandle.ToIntPtr(self), (nuint)1, IntPtr.Zero, word, IntPtr.Zero
                    });
                }
                finally { Marshal.FreeHGlobal(word); }
            };
        }

        internal string Capture() => AppleNotarizationService.ComputeArtifactSha256(_artifact);

        public void Dispose()
        {
            Monitor.Dispose();
            _root.Delete(recursive: true);
        }
    }
}
