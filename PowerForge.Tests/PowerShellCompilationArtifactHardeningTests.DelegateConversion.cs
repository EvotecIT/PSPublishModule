using System.Runtime.InteropServices;
using System.Net.Security;
using PowerForge;
using Xunit;

#pragma warning disable SYSLIB0014 // Cross-target callback contract also used by Windows PowerShell 5.1.

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactHardeningTests
{
    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void Build_StrictRuntimeFreeLibraryConvertsConstantBooleanScriptBlockToClrDelegate(string targetFramework)
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-CertificateCallback {\n" +
            "  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { param($Sender, $Certificate, $Chain, $Errors) return $true }\n" +
            "  return 42\n" +
            "}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConstantBooleanDelegate" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods()).Single(static method => method.Name == "Set_CertificateCallback");
        try
        {
            Assert.Equal(42, method.Invoke(null, null));
            var callback = System.Net.ServicePointManager.ServerCertificateValidationCallback;
            Assert.NotNull(callback);
            Assert.True(callback!(null!, null!, null!, SslPolicyErrors.None));
        }
        finally
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback = null;
        }
    }

    [Theory]
    [InlineData("net8.0", "pwsh")]
    [InlineData("net472", "powershell.exe")]
    public void Build_StrictBinaryModuleExecutesConstantBooleanDelegateOnPowerShellHosts(string targetFramework, string host)
    {
        if (targetFramework == "net472" && !OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(
            "function Set-CertificateCallback {\n" +
            "  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }\n" +
            "  return 42\n" +
            "}\n" +
            "Export-ModuleMember -Function Set-CertificateCallback",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HostConstantBooleanDelegate" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var proof = "Set-CertificateCallback; [Net.ServicePointManager]::ServerCertificateValidationCallback.Invoke($null,$null,$null,[Net.Security.SslPolicyErrors]::None); [Net.ServicePointManager]::ServerCertificateValidationCallback = $null";
        var original = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{fixture.ScriptPath.Replace("'", "''", StringComparison.Ordinal)}' -Force; {proof}");
        var compiled = Run(
            host,
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; {proof}");
        Assert.Equal(0, original.ExitCode);
        Assert.Equal(new[] { "42", "True" }, original.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(0, compiled.ExitCode);
        Assert.Equal(new[] { "42", "True" }, compiled.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(original.StandardError), original.StandardError);
        Assert.True(string.IsNullOrWhiteSpace(compiled.StandardError), compiled.StandardError);
    }

    [Fact]
    public void Build_HybridModuleCompilesBoundedDelegateAndPreservesCapturedDelegateFallback()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-CompiledCallback {\n" +
            "  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }\n" +
            "  return 42\n" +
            "}\n" +
            "function Set-FallbackCallback {\n" +
            "  $Decision = $false\n" +
            "  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { return $Decision }\n" +
            "  return 7\n" +
            "}\n" +
            "Export-ModuleMember -Function @('Set-CompiledCallback', 'Set-FallbackCallback')",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridBooleanDelegate",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(1, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.FallbackUnits);
        var escapedPath = result.ArtifactPath!.Replace("'", "''", StringComparison.Ordinal);
        var run = Run(
            "pwsh",
            "-NoProfile",
            "-NonInteractive",
            "-Command",
            $"Import-Module -Name '{escapedPath}' -Force; Set-CompiledCallback; Set-FallbackCallback; [Net.ServicePointManager]::ServerCertificateValidationCallback.Invoke($null,$null,$null,[Net.Security.SslPolicyErrors]::None); [Net.ServicePointManager]::ServerCertificateValidationCallback = $null");
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(new[] { "42", "7", "False" }, run.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(string.IsNullOrWhiteSpace(run.StandardError), run.StandardError);
    }

    [Theory]
    [InlineData("{ param($Value) return $true }")]
    [InlineData("{ param([object] $Sender, $Certificate, $Chain, $Errors) return $true }")]
    [InlineData("{ param($Sender, $Certificate, $Chain, $Errors) return $Sender -ne $null }")]
    [InlineData("{ param($Sender, $Certificate, $Chain, $Errors) $true; $false }")]
    [InlineData("{ param($global:Sender, $Certificate, $Chain, $Errors) return $true }")]
    [InlineData("{ param($true, $Certificate, $Chain, $Errors) return $true }")]
    [InlineData("{ $true > out.txt }")]
    [InlineData("{ return $true 2>$null }")]
    [InlineData("{ $true & }")]
    public void Build_StrictRejectsBooleanDelegateShapesOutsideBoundedContract(string scriptBlock)
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-CertificateCallback {\n" +
            $"  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {scriptBlock}\n" +
            "  return 42\n" +
            "}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RejectedBooleanDelegate",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SingleFile = false
        });

        Assert.False(result.Succeeded);
        Assert.Contains("delegate", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Theory]
    [InlineData("{ param($global:Sender, $Certificate, $Chain, $Errors) return $true }")]
    [InlineData("{ param($true, $Certificate, $Chain, $Errors) return $true }")]
    [InlineData("{ $true > out.txt }")]
    [InlineData("{ $true & }")]
    public void Build_HybridPreservesDelegateShapesOutsideBoundedContract(string scriptBlock)
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-CertificateCallback {\n" +
            $"  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {scriptBlock}\n" +
            "  return 42\n" +
            "}\n" +
            "Export-ModuleMember -Function Set-CertificateCallback",
            ".psm1");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.HybridRejectedBooleanDelegate",
            PowerShellCompilationArtifactKind.BinaryModule,
            PowerShellCompilationMode.Hybrid,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net8.0"
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(0, result.Manifest!.CompiledMethods);
        Assert.Equal(1, result.Manifest.FallbackUnits);
    }

    [Fact]
    public void Build_StrictAllocatesCollisionSafeDelegateParameterNames()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-CertificateCallback {\n" +
            "  $__pf_delegate_argument_0 = 42\n" +
            "  [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { param($Sender, $Certificate, $Chain, $Errors) return $true }\n" +
            "  return $__pf_delegate_argument_0\n" +
            "}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.CollisionSafeBooleanDelegate",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        var assembly = System.Reflection.Assembly.LoadFrom(result.ArtifactPath!);
        var method = assembly.GetTypes().SelectMany(static type => type.GetMethods()).Single(static method => method.Name == "Set_CertificateCallback");
        try
        {
            Assert.Equal(42, method.Invoke(null, null));
            Assert.True(System.Net.ServicePointManager.ServerCertificateValidationCallback!(null!, null!, null!, SslPolicyErrors.None));
        }
        finally
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback = null;
        }
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net10.0")]
    public void GeneratedMemberPolicyMatchesStructuralGenericPropertySignatures(string targetFramework)
    {
        var property = typeof(System.Net.Http.HttpClientHandler).GetProperty(nameof(System.Net.Http.HttpClientHandler.ServerCertificateCustomValidationCallback));

        Assert.NotNull(property);
        Assert.True(PowerShellGeneratedMemberPolicy.IsSupported(property!, targetFramework));
        Assert.True(PowerShellGeneratedMemberPolicy.IsWritableSupported(property!, targetFramework));
        Assert.True(PowerShellGeneratedTypePolicy.IsSupportedDelegateSignature(property!.PropertyType, targetFramework));

        var readOnly = typeof(string).GetProperty(nameof(string.Length));
        Assert.NotNull(readOnly);
        Assert.True(PowerShellGeneratedMemberPolicy.IsSupported(readOnly!, targetFramework));
        Assert.False(PowerShellGeneratedMemberPolicy.IsWritableSupported(readOnly!, targetFramework));
    }

    [Fact]
    public void Build_StrictRejectsNonBooleanDelegateTarget()
    {
        using var fixture = ArtifactFixture.Create(
            "function Set-ConnectCallback {\n" +
            "  $Handler = [System.Net.Http.SocketsHttpHandler]::new()\n" +
            "  $Handler.ConnectCallback = { $true }\n" +
            "  return 42\n" +
            "}");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.RejectedNonBooleanDelegate",
            PowerShellCompilationArtifactKind.Library,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            SingleFile = false
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Boolean-returning delegate", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.OutputPath));
    }

    [Fact]
    public void Build_StrictNativeAotExecutableRunsConstantBooleanDelegate()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        using var fixture = ArtifactFixture.Create(
            "[System.Func[System.Net.Http.HttpRequestMessage,System.Security.Cryptography.X509Certificates.X509Certificate2,System.Security.Cryptography.X509Certificates.X509Chain,System.Net.Security.SslPolicyErrors,bool]] $Callback = { $true }\n" +
            "$Handler = [System.Net.Http.HttpClientHandler]::new()\n" +
            "$Handler.ServerCertificateCustomValidationCallback = { $true }\n" +
            "$Request = [System.Net.Http.HttpRequestMessage]::new()\n" +
            "$CertificateBytes = [System.Convert]::FromBase64String('MIICtDCCAZygAwIBAgIIAUW2BDTBBNkwDQYJKoZIhvcNAQELBQAwGjEYMBYGA1UEAxMPUG93ZXJGb3JnZSBUZXN0MB4XDTI2MDkwMTIxMDA1NVoXDTI2MDkwMzIxMDA1NVowGjEYMBYGA1UEAxMPUG93ZXJGb3JnZSBUZXN0MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqfwxfBrmy4yCWtRs5T6745QMwOc+GFTy0Qq+ORYD/uK95PoFPa6fnD4/1feq58GTTZL4yhBaI30Kdq9gsM4iOiu8RLa390eMIUpcHh/EL50HjYs0poSosc0eeFz/vDg9mT1LkUW9zaVTR0bQv7A52otex19wuxsNyLGdHmyZyS15j5zJc3qweRrKGyLIlZMqkuzUEoYWPanxEpQJTlH8c1IYQra2ptf2lzYs9fLjbmtjnRqz416Fh2W5gxDeJqA0TMFhX3IUlDywXwZ9iU+OFAjgo/7qVEJxSA/QxEkfWBcGgKTzCZU69B0bGppB/S3396tL0nKYgme1uLBBT24bzQIDAQABMA0GCSqGSIb3DQEBCwUAA4IBAQA3HvTXac92dXznSMvbUOBvD9DI66lmpD8nVo4VhIWey1Cv7czVhoXulbu9Pfc/WzGStK0x3/St5ipTGfxyE6gJv4+++8OadK6S4lDjNacoAoBn0/8fhbm8kHYPIPi/HlcL/lncUoPQSq6mdiHYW6xaWW0BGQEDcE5cxkNnuWUAtPa16HYzFWuDg4qUWbNCOzFkogafVCnmJUXSsvGFJpYeD0LtCo/3trFyFpKn5rssULClZNu9MmVz9KUkrUyMlfm5/CDVLfzFcEdoBaYVgFlpca1SU/Wv4pZUhuWU5M7oBdDbYd+u3MPJMHiUw1AASWcVjBYGj6SfBK/vXgAHGOey')\n" +
            "$Certificate = [System.Security.Cryptography.X509Certificates.X509CertificateLoader]::LoadCertificate($CertificateBytes)\n" +
            "$Chain = [System.Security.Cryptography.X509Certificates.X509Chain]::new()\n" +
            "if (-not $Callback.Invoke($Request, $Certificate, $Chain, [System.Net.Security.SslPolicyErrors]::None)) { return 1 }\n" +
            "return 42");
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConstantBooleanDelegateNativeAot",
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = "net10.0",
            RuntimeIdentifier = "win-x64",
            SelfContained = true,
            SingleFile = true,
            Optimization = PowerShellCompilationExecutableOptimization.NativeAot,
            TimeoutSeconds = 600
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.Equal(PowerShellCompilationDeploymentModel.NativeAot, result.Manifest!.TargetContract!.Deployment);
        Assert.False(result.Manifest.RequiresPowerShellRuntime);
        var run = Run(result.ArtifactPath!);
        Assert.Equal((0, "42", string.Empty), (run.ExitCode, run.StandardOutput.Trim(), run.StandardError.Trim()));
    }

    [Fact]
    public void ConstantBooleanDelegateHasStructuredHostAndRuntimeFreeDifferentialEvidence()
    {
        const string targetFramework = "net10.0";
        const string profileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId;
        using var fixture = ArtifactFixture.Create(
            "[System.Net.Security.RemoteCertificateValidationCallback] $Callback = { param($Sender, $Certificate, $Chain, $Errors) return $true }\n" +
            "[System.Net.ServicePointManager]::ServerCertificateValidationCallback = $Callback\n" +
            "if (-not $Callback.Invoke([object] $null, [System.Security.Cryptography.X509Certificates.X509Certificate] $null, [System.Security.Cryptography.X509Certificates.X509Chain] $null, [System.Net.Security.SslPolicyErrors]::None)) { return 1 }\n" +
            "42");
        var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
            new PowerShellCompilationSemanticOracleRequest(profileId, fixture.ScriptPath)
            {
                Culture = "en-US"
            });
        var result = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
            fixture.ScriptPath,
            fixture.OutputPath,
            "PowerForge.ConstantBooleanDelegateOracle" + targetFramework.Replace(".", string.Empty, StringComparison.Ordinal),
            PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true)
        {
            TargetFramework = targetFramework,
            SemanticProfileId = profileId,
            SingleFile = false
        });

        Assert.True(result.Succeeded, result.Error + Environment.NewLine + result.BuildOutput);
        Assert.False(result.Manifest!.RequiresPowerShellRuntime);
        var strict = new PowerShellCompilationSemanticRuntimeFreeArtifactObserver().Observe(profileId, result);
        Assert.Empty(PowerShellCompilationSemanticOracleComparer.Compare(interpreted, strict, new[] { "Encoding", "ExitCode" }));
        var value = Assert.Single(strict.Success);
        Assert.Equal(("42", typeof(int).FullName), (value.Value, value.TypeName));
    }
}

#pragma warning restore SYSLIB0014
