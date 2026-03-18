using PowerForgeStudio.Orchestrator.Terminal;

namespace PowerForgeStudio.Wpf.ViewModels.Hub;

public sealed class TerminalTabViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ITerminalSession _session;
    private bool _isConnected = true;
    private string _title;
    private bool _disposed;

    public TerminalTabViewModel(ITerminalSession session, string workingDirectory)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        WorkingDirectory = workingDirectory;
        _title = $"Terminal — {System.IO.Path.GetFileName(workingDirectory)}";

        _session.OutputReceived += OnOutputReceived;
        _session.Exited += OnExited;
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    public string WorkingDirectory { get; }

    /// <summary>
    /// Raised when batched output bytes are available for the view to send to xterm.js.
    /// The byte[] is raw ConPTY output (VT sequences), base64 encode for JS transport.
    /// </summary>
    public event Action<byte[]>? OutputAvailable;

    public void SendInput(byte[] data)
    {
        if (_disposed) return;
        _session.WriteInput(data);
    }

    public void Resize(int cols, int rows)
    {
        if (_disposed || cols <= 0 || rows <= 0) return;
        _session.Resize(cols, rows);
    }

    private void OnOutputReceived(byte[] data)
    {
        OutputAvailable?.Invoke(data);
    }

    private void OnExited(int exitCode)
    {
        IsConnected = false;
        Title = $"Terminal (exited: {exitCode})";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _session.OutputReceived -= OnOutputReceived;
        _session.Exited -= OnExited;

        await _session.DisposeAsync().ConfigureAwait(false);
    }
}
