using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int CommandDotNetReleaseArtifact(string[] args, CliOptions cli, ILogger logger)
    {
        if (args.Length == 0 || !args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(DotNetReleaseArtifactVerifyUsage);
            return 2;
        }

        var commandArgs = args.Skip(1).ToArray();
        var outputJson = IsJsonOutput(commandArgs);
        var kind = TryGetOptionValue(commandArgs, "--kind");
        if (!string.IsNullOrWhiteSpace(kind))
            return CommandGeneralReleaseArtifactVerification(commandArgs, kind!, outputJson, logger);

        var projectRoot = TryGetOptionValue(commandArgs, "--project-root");
        var manifestPath = TryGetOptionValue(commandArgs, "--manifest");
        var checksumsPath = TryGetOptionValue(commandArgs, "--checksums");
        var configurationPath = TryGetOptionValue(commandArgs, "--config");
        var installerId = TryGetOptionValue(commandArgs, "--installer");
        var sourceRevision = TryGetOptionValue(commandArgs, "--source-revision");
        var target = TryGetOptionValue(commandArgs, "--target");
        var runtime = TryGetOptionValue(commandArgs, "--rid");
        var framework = TryGetOptionValue(commandArgs, "--framework");
        var style = TryGetOptionValue(commandArgs, "--style");
        var artifactPath = TryGetOptionValue(commandArgs, "--artifact");
        var profile = TryGetOptionValue(commandArgs, "--profile");
        var signProfile = TryGetOptionValue(commandArgs, "--sign-profile");
        var signThumbprint = TryGetOptionValue(commandArgs, "--sign-thumbprint");
        var signSubjectName = TryGetOptionValue(commandArgs, "--sign-subject-name");
        var enableSigning = commandArgs.Any(value => value.Equals("--sign", StringComparison.OrdinalIgnoreCase));
        var disableSigning = commandArgs.Any(value => value.Equals("--no-sign", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            string.IsNullOrWhiteSpace(manifestPath) ||
            string.IsNullOrWhiteSpace(checksumsPath) ||
            string.IsNullOrWhiteSpace(configurationPath) ||
            string.IsNullOrWhiteSpace(installerId) ||
            string.IsNullOrWhiteSpace(sourceRevision) ||
            !IsFullGitObjectId(sourceRevision) ||
            (enableSigning && disableSigning))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "dotnet.release-artifact.verify",
                    Success = false,
                    ExitCode = 2,
                    Error = !string.IsNullOrWhiteSpace(sourceRevision) && !IsFullGitObjectId(sourceRevision)
                        ? "Source revision must be a full 40- or 64-character hexadecimal Git object ID."
                        : enableSigning && disableSigning
                        ? "Use either --sign or --no-sign, not both."
                        : "Project root, manifest, checksums, config, installer, and source revision are required."
                });
            }
            else
            {
                Console.WriteLine(DotNetReleaseArtifactVerifyUsage);
            }

            return 2;
        }

        try
        {
            DotNetPublishReleaseArtifact result = new DotNetPublishReleaseArtifactVerifier().Verify(
                new DotNetPublishReleaseArtifactVerificationRequest
                {
                    ProjectRoot = projectRoot,
                    ManifestPath = manifestPath,
                    ChecksumsPath = checksumsPath,
                    ConfigurationPath = configurationPath,
                    InstallerId = installerId,
                    ExpectedSourceRevision = sourceRevision,
                    Target = target,
                    Runtime = runtime,
                    Framework = framework,
                    Style = style,
                    ArtifactPath = artifactPath,
                    Profile = profile,
                    SignProfile = signProfile,
                    SignThumbprint = signThumbprint,
                    SignSubjectName = signSubjectName,
                    EnableSigning = enableSigning ? true : disableSigning ? false : null
                });
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "dotnet.release-artifact.verify",
                    Success = true,
                    ExitCode = 0,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.DotNetPublishReleaseArtifact)
                });
            }
            else
            {
                logger.Success($"Verified {result.InstallerId} {result.Version}: {result.ArtifactPath}");
                logger.Info($"SHA-256: {result.Sha256}");
                logger.Info($"Source: {result.SourceRevision}");
                logger.Info($"Signer: {result.SignerSubject} ({result.SignerThumbprint})");
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "dotnet.release-artifact.verify",
                    Success = false,
                    ExitCode = 1,
                    Error = ex.Message
                });
            }
            else
            {
                logger.Error(ex.Message);
            }

            return 1;
        }
    }

    private static int CommandGeneralReleaseArtifactVerification(
        string[] commandArgs,
        string kindValue,
        bool outputJson,
        ILogger logger)
    {
        if (!TryParseReleaseArtifactKind(kindValue, out PowerForgeReleaseArtifactKind kind))
        {
            return WriteGeneralReleaseArtifactError(
                outputJson,
                logger,
                2,
                "Release artifact kind must be portable-cli or powershell-module.");
        }
        var projectRoot = TryGetOptionValue(commandArgs, "--project-root");
        var artifactId = TryGetOptionValue(commandArgs, "--artifact-id");
        var artifactPath = TryGetOptionValue(commandArgs, "--artifact");
        var checksumsPath = TryGetOptionValue(commandArgs, "--checksums");
        var sourceRevision = TryGetOptionValue(commandArgs, "--source-revision");
        var manifestPath = TryGetOptionValue(commandArgs, "--manifest");
        var configurationPath = TryGetOptionValue(commandArgs, "--config");
        var signingEvidencePath = TryGetOptionValue(commandArgs, "--signing-evidence");
        var signThumbprint = TryGetOptionValue(commandArgs, "--sign-thumbprint");
        var signSubjectName = TryGetOptionValue(commandArgs, "--sign-subject-name");
        var enableSigning = commandArgs.Any(value => value.Equals("--sign", StringComparison.OrdinalIgnoreCase));
        var disableSigning = commandArgs.Any(value => value.Equals("--no-sign", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(projectRoot) ||
            string.IsNullOrWhiteSpace(artifactId) ||
            string.IsNullOrWhiteSpace(artifactPath) ||
            string.IsNullOrWhiteSpace(checksumsPath) ||
            string.IsNullOrWhiteSpace(sourceRevision) ||
            !IsFullGitObjectId(sourceRevision) ||
            (kind == PowerForgeReleaseArtifactKind.PortableCli &&
             (string.IsNullOrWhiteSpace(manifestPath) || string.IsNullOrWhiteSpace(configurationPath))) ||
            (kind == PowerForgeReleaseArtifactKind.PowerShellModule &&
             (string.IsNullOrWhiteSpace(signingEvidencePath) ||
              (string.IsNullOrWhiteSpace(signThumbprint) && string.IsNullOrWhiteSpace(signSubjectName)))) ||
            (enableSigning && disableSigning))
        {
            return WriteGeneralReleaseArtifactError(
                outputJson,
                logger,
                2,
                !string.IsNullOrWhiteSpace(sourceRevision) && !IsFullGitObjectId(sourceRevision)
                    ? "Source revision must be a full 40- or 64-character hexadecimal Git object ID."
                    : enableSigning && disableSigning
                    ? "Use either --sign or --no-sign, not both."
                    : kind == PowerForgeReleaseArtifactKind.PowerShellModule &&
                      string.IsNullOrWhiteSpace(signThumbprint) && string.IsNullOrWhiteSpace(signSubjectName)
                        ? "PowerShell module verification requires --sign-thumbprint or --sign-subject-name."
                        : "Kind, artifact ID, project root, artifact, checksums, source revision, portable manifest/config, and module signing evidence are required for their respective artifact kinds.");
        }
        if (!OperatingSystem.IsWindows())
        {
            return WriteGeneralReleaseArtifactError(
                outputJson,
                logger,
                2,
                "Release artifact Authenticode verification is currently supported only on Windows.");
        }

        try
        {
            PowerForgeReleaseArtifactEvidence result = new PowerForgeReleaseArtifactVerifier().Verify(
                new PowerForgeReleaseArtifactVerificationRequest
                {
                    Kind = kind,
                    ArtifactId = artifactId!,
                    ProjectRoot = projectRoot!,
                    ArtifactPath = artifactPath!,
                    ChecksumsPath = checksumsPath!,
                    ExpectedSourceRevision = sourceRevision!,
                    ExpectedVersion = TryGetOptionValue(commandArgs, "--version"),
                    ManifestPath = manifestPath,
                    ConfigurationPath = configurationPath,
                    SigningEvidencePath = signingEvidencePath,
                    Target = TryGetOptionValue(commandArgs, "--target"),
                    Runtime = TryGetOptionValue(commandArgs, "--rid"),
                    Framework = TryGetOptionValue(commandArgs, "--framework"),
                    Style = TryGetOptionValue(commandArgs, "--style"),
                    Profile = TryGetOptionValue(commandArgs, "--profile"),
                    SignProfile = TryGetOptionValue(commandArgs, "--sign-profile"),
                    SignThumbprint = signThumbprint,
                    SignSubjectName = signSubjectName,
                    EnableSigning = enableSigning ? true : disableSigning ? false : null,
                    SignaturePaths = ParseRepeatedOptionValues(commandArgs, "--signature-path"),
                    SbomPaths = ParseRepeatedOptionValues(commandArgs, "--sbom")
                });
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "dotnet.release-artifact.verify",
                    Success = true,
                    ExitCode = 0,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.PowerForgeReleaseArtifactEvidence)
                });
            }
            else
            {
                logger.Success($"Verified {result.ArtifactKind} {result.ArtifactId} {result.Version}: {result.ArtifactPath}");
                logger.Info($"SHA-256: {result.Sha256}");
                logger.Info($"Source: {result.SourceRevision}");
                logger.Info($"Signer: {result.SignerSubject} ({result.SignerThumbprint})");
            }
            return 0;
        }
        catch (Exception exception)
        {
            return WriteGeneralReleaseArtifactError(outputJson, logger, 1, exception.Message);
        }
    }

    private static bool IsFullGitObjectId(string? value)
    {
        string candidate = value?.Trim() ?? string.Empty;
        return (candidate.Length == 40 || candidate.Length == 64) && candidate.All(Uri.IsHexDigit);
    }

    private static int WriteGeneralReleaseArtifactError(bool outputJson, ILogger logger, int exitCode, string error)
    {
        if (outputJson)
        {
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion,
                Command = "dotnet.release-artifact.verify",
                Success = false,
                ExitCode = exitCode,
                Error = error
            });
        }
        else if (exitCode == 2)
        {
            logger.Error(error);
            Console.WriteLine(DotNetReleaseArtifactVerifyUsage);
        }
        else
        {
            logger.Error(error);
        }
        return exitCode;
    }

    private static bool TryParseReleaseArtifactKind(string value, out PowerForgeReleaseArtifactKind kind)
    {
        string normalized = value.Trim().Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalized.Equals("portablecli", StringComparison.OrdinalIgnoreCase))
        {
            kind = PowerForgeReleaseArtifactKind.PortableCli;
            return true;
        }
        if (normalized.Equals("powershellmodule", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("module", StringComparison.OrdinalIgnoreCase))
        {
            kind = PowerForgeReleaseArtifactKind.PowerShellModule;
            return true;
        }
        kind = default;
        return false;
    }
}
