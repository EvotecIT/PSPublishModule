using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace PowerForge;

/// <summary>Scopes opt-in Windows process placement around a complete benchmark run.</summary>
internal sealed class PowerShellBenchmarkProcessPlacement : IDisposable
{
    private static int _active;
    private readonly Process _process;
    private readonly IntPtr _originalAffinity;
    private readonly ProcessPriorityClass _originalPriority;
    private bool _affinityChanged;
    private bool _priorityChanged;
    private bool _disposed;

    private PowerShellBenchmarkProcessPlacement(PowerShellBenchmarkSuite suite)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException();
        _process = Process.GetCurrentProcess();
        try
        {
            _originalAffinity = _process.ProcessorAffinity;
            _originalPriority = _process.PriorityClass;
            if (suite.ProcessorAffinityMask.HasValue)
            {
                ulong requested = suite.ProcessorAffinityMask.Value;
                ulong available = ToMask(_originalAffinity);
                if (requested == 0 || (requested & available) != requested)
                    throw new ArgumentOutOfRangeException(nameof(suite.ProcessorAffinityMask),
                        "The affinity mask must select processors in the process's current affinity mask.");
                var affinity = IntPtr.Size == 8
                    ? new IntPtr(unchecked((long)requested))
                    : new IntPtr(unchecked((int)requested));
                if (affinity != _originalAffinity)
                {
                    _process.ProcessorAffinity = affinity;
                    _affinityChanged = true;
                }
                _process.Refresh();
                if (_process.ProcessorAffinity != affinity)
                    throw new InvalidOperationException("The requested process affinity was not applied.");
            }
            if (suite.ProcessPriority.HasValue && suite.ProcessPriority.Value != _originalPriority)
            {
                _process.PriorityClass = suite.ProcessPriority.Value;
                _priorityChanged = true;
                _process.Refresh();
                if (_process.PriorityClass != suite.ProcessPriority.Value)
                    throw new InvalidOperationException("The requested process priority was not applied.");
            }
        }
        catch (Exception error)
        {
            try { Dispose(); }
            catch (Exception restoreError) { throw new AggregateException(error, restoreError); }
            throw;
        }
    }

    internal static PowerShellBenchmarkProcessPlacement? Enter(PowerShellBenchmarkSuite suite)
    {
        if (!suite.ProcessorAffinityMask.HasValue && !suite.ProcessPriority.HasValue) return null;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Benchmark process placement is supported on Windows only.");
        if (GetActiveProcessorGroupCount() != 1)
            throw new PlatformNotSupportedException("Benchmark process placement requires one Windows processor group.");
        if (suite.ProcessPriority.HasValue &&
            !Enum.IsDefined(typeof(ProcessPriorityClass), suite.ProcessPriority.Value))
            throw new ArgumentOutOfRangeException(nameof(suite.ProcessPriority));
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            throw new InvalidOperationException("Another benchmark is already controlling this process's placement. Use a separate host process.");
        return new PowerShellBenchmarkProcessPlacement(suite);
    }

    internal void RecordMetadata(Dictionary<string, string> metadata)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException();
        _process.Refresh();
        metadata["processAffinityMask"] = FormatMask(_process.ProcessorAffinity);
        metadata["processPriority"] = _process.PriorityClass.ToString();
        metadata["originalProcessAffinityMask"] = FormatMask(_originalAffinity);
        metadata["originalProcessPriority"] = _originalPriority.ToString();
    }

    public void Dispose()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException();
        if (_disposed) return;
        _disposed = true;
        var failures = new List<Exception>();
        try
        {
            if (_priorityChanged)
            {
                try { _process.PriorityClass = _originalPriority; }
                catch (Exception error) { failures.Add(error); }
            }
            if (_affinityChanged)
            {
                try { _process.ProcessorAffinity = _originalAffinity; }
                catch (Exception error) { failures.Add(error); }
            }
        }
        finally
        {
            _process.Dispose();
            Volatile.Write(ref _active, 0);
        }
        if (failures.Count > 0)
            throw new AggregateException("Failed to restore the benchmark process's original placement.", failures);
    }

    private static ulong ToMask(IntPtr value)
        => IntPtr.Size == 8 ? unchecked((ulong)value.ToInt64()) : unchecked((uint)value.ToInt32());

    private static string FormatMask(IntPtr value) => "0x" + ToMask(value).ToString("X", CultureInfo.InvariantCulture);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern ushort GetActiveProcessorGroupCount();
}
