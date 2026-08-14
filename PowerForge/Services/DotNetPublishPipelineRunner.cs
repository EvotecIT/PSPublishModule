using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Plans and executes a configuration-driven dotnet publish workflow using <c>dotnet</c>.
/// </summary>
public sealed partial class DotNetPublishPipelineRunner
{
    private readonly ILogger _logger;
    private readonly IProcessRunner _processRunner;
    private readonly Func<string, bool> _hasAuthenticodeSignature;
    private readonly Func<string, DotNetPublishSignOptions, bool> _signatureMatchesPublisher;
    private readonly Func<byte[], DotNetPublishSignOptions, byte[]> _signPortableInventory;
    private readonly Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult> _readAuthenticodeSignature;
    private readonly AsyncLocal<CancellationToken> _cancellationToken = new();

    /// <summary>
    /// Creates a new instance using the provided logger.
    /// </summary>
    public DotNetPublishPipelineRunner(ILogger logger)
        : this(logger, new ProcessRunner())
    {
    }

    internal DotNetPublishPipelineRunner(
        ILogger logger,
        IProcessRunner processRunner,
        Func<string, bool>? hasAuthenticodeSignature = null,
        Func<byte[], DotNetPublishSignOptions, byte[]>? signPortableInventory = null,
        Func<string, DotNetPublishSignOptions, bool>? signatureMatchesPublisher = null,
        Func<string, DotNetPublishReleaseArtifactVerifier.AuthenticodeResult>? readAuthenticodeSignature = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _hasAuthenticodeSignature = hasAuthenticodeSignature ?? WindowsAuthenticodeSignatureInspector.HasSignature;
        _signatureMatchesPublisher = signatureMatchesPublisher ?? SignatureMatchesPublisher;
        _signPortableInventory = signPortableInventory ?? PowerForgePortablePayloadInventoryCms.Sign;
        _readAuthenticodeSignature = readAuthenticodeSignature ?? DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode;
    }

}
