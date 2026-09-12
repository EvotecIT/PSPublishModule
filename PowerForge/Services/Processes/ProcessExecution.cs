using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace PowerForge;

/// <summary>One process and its redirected streams, optionally backed by an owned OS process scope.</summary>
internal abstract class ProcessExecution : IDisposable
{
    protected ProcessExecution(ProcessStartInfo startInfo) => StartInfo = startInfo;
    internal ProcessStartInfo StartInfo { get; }
    internal bool RequireDirectStart { get; set; }
    internal abstract int Id { get; }
    internal abstract bool HasExited { get; }
    internal abstract int ExitCode { get; }
    internal abstract StreamReader StandardOutput { get; }
    internal abstract StreamReader StandardError { get; }
    internal abstract void Start();
    internal abstract void KillTree();
    public abstract void Dispose();

    internal static ProcessExecution Create(ProcessStartInfo startInfo, bool ownProcessTree)
        => !ownProcessTree ? new ManagedProcessExecution(startInfo)
            : FrameworkCompatibility.IsWindows() ? new WindowsOwnedProcessExecution(startInfo)
            : new UnixOwnedProcessExecution(startInfo);

    internal void WaitForExit(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (!HasExited && watch.ElapsedMilliseconds < milliseconds) Thread.Sleep(10);
    }

    private sealed class ManagedProcessExecution : ProcessExecution
    {
        private readonly Process _process;
        internal ManagedProcessExecution(ProcessStartInfo startInfo) : base(startInfo) => _process = new Process { StartInfo = startInfo };
        internal override int Id => _process.Id;
        internal override bool HasExited => _process.HasExited;
        internal override int ExitCode => _process.ExitCode;
        internal override StreamReader StandardOutput => _process.StandardOutput;
        internal override StreamReader StandardError => _process.StandardError;
        internal override void Start() => _process.Start();
        internal override void KillTree() => ProcessRunner.TryKillManagedProcess(_process);
        public override void Dispose() => _process.Dispose();
    }
}

/// <summary>Owns pipe handles for a native launch; only the parent's reading ends survive launch.</summary>
internal abstract class OwnedProcessExecution : ProcessExecution
{
    protected OwnedProcessExecution(ProcessStartInfo startInfo) : base(startInfo) { }
    protected AnonymousPipeServerStream? OutputPipe { get; private set; }
    protected AnonymousPipeServerStream? ErrorPipe { get; private set; }
    private StreamReader? _output;
    private StreamReader? _error;
    internal override StreamReader StandardOutput => _output ?? throw new InvalidOperationException("Standard output is not redirected.");
    internal override StreamReader StandardError => _error ?? throw new InvalidOperationException("Standard error is not redirected.");

    protected void OpenPipes()
    {
        // Unix dup2 actions clear CLOEXEC on the selected child descriptors. Keep all
        // original descriptors CLOEXEC so concurrent launches cannot inherit other probes' pipes.
        var inheritance = FrameworkCompatibility.IsWindows() ? HandleInheritability.Inheritable : HandleInheritability.None;
        if (StartInfo.RedirectStandardOutput)
        {
            OutputPipe = new AnonymousPipeServerStream(PipeDirection.In, inheritance);
            _output = new StreamReader(OutputPipe, StartInfo.StandardOutputEncoding ?? new UTF8Encoding(false));
        }
        if (StartInfo.RedirectStandardError)
        {
            ErrorPipe = new AnonymousPipeServerStream(PipeDirection.In, inheritance);
            _error = new StreamReader(ErrorPipe, StartInfo.StandardErrorEncoding ?? new UTF8Encoding(false));
        }
    }

    protected void CloseChildPipeHandles()
    {
        OutputPipe?.DisposeLocalCopyOfClientHandle();
        ErrorPipe?.DisposeLocalCopyOfClientHandle();
    }

    protected void ClosePipes()
    {
        _output?.Dispose();
        _error?.Dispose();
        OutputPipe?.Dispose();
        ErrorPipe?.Dispose();
    }
}
