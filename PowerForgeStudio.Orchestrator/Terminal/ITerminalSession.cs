namespace PowerForgeStudio.Orchestrator.Terminal;

public interface ITerminalSession : IAsyncDisposable
{
    bool IsRunning { get; }
    event Action<byte[]>? OutputReceived;
    event Action<int>? Exited;
    void WriteInput(byte[] data);
    void Resize(int cols, int rows);
    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);
}
