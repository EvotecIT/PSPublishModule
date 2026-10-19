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

    [Theory]
    [InlineData(0x20u, "lost continuity")]
    [InlineData(0x20003u, "lost continuity")]
    [InlineData(0x100u, "unreadable path")]
    public void Producer_buffer_loss_cannot_discard_terminal_suffix_in_same_native_batch(uint suffix, string diagnostic)
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.NativeEvents(0x3, suffix);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer"));

        Assert.Contains(diagnostic, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Producer_buffer_loss_only_batch_permits_fresh_complete_binding()
    {
        if (!OperatingSystem.IsMacOS()) return;
        using var fixture = new NativeContinuityFixture();
        fixture.NativeEvents(0x3, 0x5, 0x7);

        Assert.Equal(fixture.Capture(), fixture.Monitor.CaptureExpectedProducerOutput(fixture.Capture, "test producer"));
        fixture.Monitor.ValidateNoChanges();
    }

    private sealed class NativeContinuityFixture : IDisposable
    {
        private readonly DirectoryInfo _root;
        private readonly string _artifact;
        internal AppleReleaseSourceMutationMonitor Monitor { get; }
        internal Action<Exception> Error { get; }
        internal Action<uint> NativeError { get; }
        private readonly Action<uint[]> _nativeEvents;

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
            _nativeEvents = flags =>
            {
                // Supply actual native batches. Null path entries exercise managed path failure
                // safely after any continuity flags, without an invalid native memory address.
                var words = Marshal.AllocHGlobal(checked(flags.Length * sizeof(uint)));
                var paths = Marshal.AllocHGlobal(checked(flags.Length * IntPtr.Size));
                try
                {
                    for (var index = 0; index < flags.Length; index++)
                    {
                        Marshal.WriteInt32(words, index * sizeof(uint), unchecked((int)flags[index]));
                        Marshal.WriteIntPtr(paths, index * IntPtr.Size, IntPtr.Zero);
                    }
                    receive.Invoke(null, new object[]
                    {
                        IntPtr.Zero, GCHandle.ToIntPtr(self), (nuint)flags.Length, paths, words, IntPtr.Zero
                    });
                }
                finally
                {
                    Marshal.FreeHGlobal(paths);
                    Marshal.FreeHGlobal(words);
                }
            };
            NativeError = flags => _nativeEvents(new[] { flags });
        }

        internal void NativeEvents(params uint[] flags) => _nativeEvents(flags);

        internal string Capture() => AppleNotarizationService.ComputeArtifactSha256(_artifact);

        public void Dispose()
        {
            Monitor.Dispose();
            _root.Delete(recursive: true);
        }
    }
}
