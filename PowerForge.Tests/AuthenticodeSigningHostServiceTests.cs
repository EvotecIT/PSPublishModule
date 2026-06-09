using PowerForge;

namespace PowerForge.Tests;

public sealed class AuthenticodeSigningHostServiceTests
{
    [Fact]
    public async Task SignAsync_UsesSharedRegisterCertificateWrapper()
    {
        PowerShellRunRequest? captured = null;
        var service = new AuthenticodeSigningHostService(new StubPowerShellRunner(request => {
            captured = request;
            return new PowerShellRunResult(0, "signed", string.Empty, "pwsh");
        }));

        var result = await service.SignAsync(new AuthenticodeSigningHostRequest {
            SigningPath = @"C:\repo\Artifacts",
            IncludePatterns = ["*.ps1", "*.psd1"],
            ModulePath = @"C:\repo\Module\PSPublishModule.psd1",
            Thumbprint = "thumb",
            StoreName = "CurrentUser",
            TimeStampServer = "http://timestamp.digicert.com"
        });

        Assert.NotNull(captured);
        Assert.Equal(PowerShellInvocationMode.Command, captured!.InvocationMode);
        Assert.Equal(@"C:\repo\Artifacts", captured.WorkingDirectory);
        Assert.Contains("Register-Certificate", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("-Thumbprint 'thumb'", captured.CommandText!, StringComparison.Ordinal);
        Assert.Contains("-Include @('*.ps1', '*.psd1')", captured.CommandText!, StringComparison.Ordinal);
        Assert.True(result.Succeeded);
    }

    private sealed class StubPowerShellRunner : IPowerShellRunner
    {
        private readonly Func<PowerShellRunRequest, PowerShellRunResult> _execute;

        public StubPowerShellRunner(Func<PowerShellRunRequest, PowerShellRunResult> execute)
        {
            _execute = execute;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
            => _execute(request);
    }
}
