using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PowerForgeStudio.Domain.Connections;

namespace PowerForgeStudio.Orchestrator.Connections;

internal sealed class IntelligenceXConnectionSource(
    string pipeName = "intelligencex.chat",
    TimeSpan? connectTimeout = null) : IWorkspaceConnectionSource
{
    private const string ProbeRequestId = "powerforge-studio-capability-check";
    public string Provider => "IntelligenceX";

    public async Task<ConnectionSourceResult> ReadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var endpoint = $"named-pipe://./{pipeName}";
        string? version = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(connectTimeout ?? TimeSpan.FromSeconds(2));
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
            await writer.WriteLineAsync($"{{\"type\":\"hello\",\"requestId\":\"{ProbeRequestId}\"}}").ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(line) && line.Length <= 65_536)
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "hello" &&
                    document.RootElement.TryGetProperty("requestId", out var requestId) && requestId.GetString() == ProbeRequestId &&
                    document.RootElement.TryGetProperty("name", out var name) && name.GetString() == "IntelligenceX.Chat.Service" &&
                    document.RootElement.TryGetProperty("version", out var value)) version = value.GetString();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (IOException) { }
        catch (JsonException) { }

        var ownerAvailable = OwnerSourceAvailable(workspaceRoot);
        var verified = !string.IsNullOrWhiteSpace(version);
        var state = verified ? "Verified" : ownerAvailable ? "Available" : "Unavailable";
        var evidence = verified
            ? $"Local hello handshake completed with IntelligenceX.Chat.Service {SafeVersion(version!)}."
            : ownerAvailable ? "IntelligenceX owner source is available; the local chat service did not answer."
            : "IntelligenceX owner source and local service were not found.";
        WorkspaceConnectionEntry entry = new(
            "intelligencex:chat", "IntelligenceX", Provider, "Intelligence", state,
            ConnectionEndpointSanitizer.Sanitize(endpoint), "IntelligenceX provider-owned profile store",
            verified ? ["Service handshake"] : ownerAvailable ? ["Owner source detected"] : [], verified ? DateTimeOffset.UtcNow : null,
            evidence + " Studio did not read provider profiles or API keys.", "IntelligenceX.Chat.Client and IntelligenceX.Chat.Service");
        return new ConnectionSourceResult([entry], new WorkspaceConnectionSourceState(Provider, state, 1, evidence));
    }

    private static string SafeVersion(string value)
    {
        var safe = new string(value.Trim().Where(static character => char.IsLetterOrDigit(character) || character is '.' or '-' or '+').Take(40).ToArray());
        return safe.Length == 0 ? "unknown" : safe;
    }

    private static bool OwnerSourceAvailable(string workspaceRoot)
    {
        DirectoryInfo? directory = new(Path.GetFullPath(workspaceRoot));
        for (var level = 0; level < 3 && directory is not null; level++, directory = directory.Parent)
        {
            var root = directory.Name.Equals("IntelligenceX", StringComparison.OrdinalIgnoreCase)
                ? directory.FullName
                : Path.Combine(directory.FullName, "IntelligenceX");
            var project = Path.Combine(root, "IntelligenceX.Chat", "IntelligenceX.Chat.Service", "IntelligenceX.Chat.Service.csproj");
            if (File.Exists(project)) return true;
        }
        return false;
    }
}
