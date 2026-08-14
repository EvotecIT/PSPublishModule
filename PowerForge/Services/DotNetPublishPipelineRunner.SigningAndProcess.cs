using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private string[] TrySignOutput(string outputDir, DotNetPublishSignOptions sign)
    {
        if (sign is null || !sign.Enabled) return Array.Empty<string>();
        var targets = new List<string>();
        try
        {
            targets.AddRange(Directory.EnumerateFiles(outputDir, "*.exe", SearchOption.AllDirectories));
            if (sign.IncludeDlls)
                targets.AddRange(Directory.EnumerateFiles(outputDir, "*.dll", SearchOption.AllDirectories));
        }
        catch
        {
            // ignore
        }

        return TrySignFiles(targets, outputDir, sign, scope: "publish outputs");
    }

    private string[] TrySignFiles(
        IEnumerable<string> files,
        string workingDirectory,
        DotNetPublishSignOptions sign,
        string? scope)
    {
        if (sign is null || !sign.Enabled)
            return Array.Empty<string>();

        if (!IsWindows())
        {
            HandlePolicy(
                sign.OnMissingTool,
                "Signing requested but current OS is not Windows.");
            return Array.Empty<string>();
        }

        var signTool = ResolveSignToolPath(sign.ToolPath);
        if (string.IsNullOrWhiteSpace(signTool))
        {
            HandlePolicy(
                sign.OnMissingTool,
                "Signing requested but signtool.exe was not found.");
            return Array.Empty<string>();
        }

        var signToolPath = signTool!;
        if (!File.Exists(signToolPath))
        {
            HandlePolicy(
                sign.OnMissingTool,
                $"Signing requested but signtool.exe was not found: {signToolPath}.");
            return Array.Empty<string>();
        }

        var targets = (files ?? Array.Empty<string>())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => Path.GetFullPath(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0)
        {
            var noTargetsScope = string.IsNullOrWhiteSpace(scope) ? "outputs" : scope!.Trim();
            HandlePolicy(
                sign.OnSignFailure,
                $"Signing requested for {noTargetsScope}, but no matching files were found. Set IncludeDlls=true to include DLL outputs when a publish produces no executables.");
            return Array.Empty<string>();
        }

        var label = string.IsNullOrWhiteSpace(scope) ? string.Empty : $" ({scope!.Trim()})";
        _logger.Info($"Signing {targets.Length} file(s){label} using {Path.GetFileName(signToolPath)}");

        var signed = new List<string>(targets.Length);
        var runDir = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory;
        string? metadataRoot = null;
        string? metadataPath = null;
        string? dlibPath = null;
        Exception? signingFailure = null;
        if (sign.Provider == DotNetPublishSigningProvider.AzureArtifactSigning)
        {
            ValidateAzureArtifactSigningOptions(sign);
            dlibPath = ResolveAzureArtifactSigningDlibPath(sign.AzureArtifactSigning!.DlibPath);
            if (string.IsNullOrWhiteSpace(dlibPath))
            {
                HandlePolicy(
                    sign.OnMissingTool,
                    "Azure Artifact Signing requested but Azure.CodeSigning.Dlib.dll was not found.");
                return Array.Empty<string>();
            }
            metadataRoot = Directory.CreateDirectory(Path.Combine(
                Path.GetTempPath(),
                "PowerForge.AzureArtifactSigning",
                Guid.NewGuid().ToString("N"))).FullName;
            metadataPath = Path.Combine(metadataRoot, "metadata.json");
            WriteAzureArtifactSigningMetadata(metadataPath, sign.AzureArtifactSigning);
        }

        try
        {
            foreach (var file in targets)
            {
                if (!File.Exists(file))
                {
                    HandlePolicy(sign.OnSignFailure, $"Signing target not found: {file}");
                    continue;
                }

                if (!sign.OverwriteSigned && _hasAuthenticodeSignature(file))
                {
                    if (_logger.IsVerbose)
                        _logger.Verbose($"Preserving existing signature: {file}");
                    if (_signatureMatchesPublisher(file, sign))
                    {
                        signed.Add(file);
                    }
                    else if (_logger.IsVerbose)
                    {
                        _logger.Verbose(
                            $"Preserved signature is not owned by the configured publisher and will remain payload-bound only: {file}");
                    }
                    continue;
                }

                var timeout = TimeSpan.FromSeconds(Math.Max(1, sign.TimeoutSeconds));
                List<string> args = BuildSignToolArguments(sign, file, dlibPath, metadataPath);
                var res = RunSigningTool(
                    signToolPath,
                    runDir,
                    args,
                    timeout);
                if (res.ExitCode != 0)
                {
                    var details = TailLines(res.StdErr, maxLines: 10, maxChars: 2000) ?? string.Empty;
                    var message = string.IsNullOrWhiteSpace(details)
                        ? $"Signing failed for '{file}' (exit code: {res.ExitCode})."
                        : $"Signing failed for '{file}' (exit code: {res.ExitCode}). {details.Trim()}";
                    HandlePolicy(sign.OnSignFailure, message);
                    continue;
                }

                signed.Add(file);
            }
        }
        catch (Exception exception)
        {
            signingFailure = exception;
            throw;
        }
        finally
        {
            DeleteAzureArtifactSigningMetadata(metadataRoot, signingFailure);
        }

        return signed
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal byte[] SignPortableInventory(byte[] content, DotNetPublishSignOptions sign)
    {
        if (sign.Provider != DotNetPublishSigningProvider.AzureArtifactSigning)
            return PowerForgePortablePayloadInventoryCms.Sign(content, sign);

        ValidateAzureArtifactSigningOptions(sign);
        string signToolPath = ResolveSignToolPath(sign.ToolPath)
            ?? throw new InvalidOperationException("Azure Artifact Signing requested but signtool.exe was not found.");
        string dlibPath = ResolveAzureArtifactSigningDlibPath(sign.AzureArtifactSigning!.DlibPath)
            ?? throw new InvalidOperationException("Azure Artifact Signing requested but Azure.CodeSigning.Dlib.dll was not found.");
        string tempRoot = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "PowerForge.AzureArtifactSigning",
            Guid.NewGuid().ToString("N"))).FullName;
        Exception? signingFailure = null;
        try
        {
            string inventoryPath = Path.Combine(tempRoot, PowerForgePortablePayloadInventory.InventoryFileName);
            string metadataPath = Path.Combine(tempRoot, "metadata.json");
            string signatureRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "signature")).FullName;
            File.WriteAllBytes(inventoryPath, content);
            WriteAzureArtifactSigningMetadata(metadataPath, sign.AzureArtifactSigning);
            List<string> arguments = BuildSignToolArguments(sign, inventoryPath, dlibPath, metadataPath);
            arguments.InsertRange(arguments.Count - 1, new[]
            {
                "/p7", signatureRoot,
                "/p7ce", "DetachedSignedData",
                "/p7co", "1.3.6.1.5.5.7.3.3"
            });
            ProcessRunResult result = RunSigningTool(
                signToolPath,
                tempRoot,
                arguments,
                TimeSpan.FromSeconds(Math.Max(1, sign.TimeoutSeconds)));
            if (result.ExitCode != 0)
            {
                string details = TailLines(result.StdErr, maxLines: 10, maxChars: 2000) ?? string.Empty;
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(details)
                        ? $"Azure Artifact Signing failed for the portable inventory (exit code: {result.ExitCode})."
                        : $"Azure Artifact Signing failed for the portable inventory (exit code: {result.ExitCode}). {details.Trim()}");
            }

            string[] signatures = Directory.GetFiles(signatureRoot, "*.p7", SearchOption.TopDirectoryOnly);
            if (signatures.Length != 1)
                throw new InvalidOperationException("Azure Artifact Signing did not produce exactly one detached portable inventory signature.");
            return File.ReadAllBytes(signatures[0]);
        }
        catch (Exception exception)
        {
            signingFailure = exception;
            throw;
        }
        finally
        {
            DeleteAzureArtifactSigningMetadata(tempRoot, signingFailure);
        }
    }

    private static List<string> BuildSignToolArguments(
        DotNetPublishSignOptions sign,
        string file,
        string? dlibPath,
        string? metadataPath)
    {
        var args = new List<string> { "sign", "/fd", "SHA256" };
        if (!string.IsNullOrWhiteSpace(sign.TimestampUrl))
            args.AddRange(new[] { "/tr", sign.TimestampUrl!, "/td", "SHA256" });
        if (!string.IsNullOrWhiteSpace(sign.Description))
            args.AddRange(new[] { "/d", sign.Description! });
        if (!string.IsNullOrWhiteSpace(sign.Url))
            args.AddRange(new[] { "/du", sign.Url! });

        if (sign.Provider == DotNetPublishSigningProvider.AzureArtifactSigning)
        {
            args.AddRange(new[] { "/dlib", dlibPath!, "/dmdf", metadataPath! });
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(sign.Thumbprint))
                args.AddRange(new[] { "/sha1", sign.Thumbprint! });
            else if (!string.IsNullOrWhiteSpace(sign.SubjectName))
                args.AddRange(new[] { "/n", sign.SubjectName! });
            else
                args.Add("/a");

            if (!string.IsNullOrWhiteSpace(sign.Csp))
                args.AddRange(new[] { "/csp", sign.Csp! });
            if (!string.IsNullOrWhiteSpace(sign.KeyContainer))
                args.AddRange(new[] { "/kc", sign.KeyContainer! });
        }

        args.Add(file);
        return args;
    }

    private static void ValidateAzureArtifactSigningOptions(DotNetPublishSignOptions sign)
    {
        DotNetPublishAzureArtifactSigningOptions options = sign.AzureArtifactSigning
            ?? throw new ArgumentException("Azure Artifact Signing settings are required when that provider is selected.");
        if (!Uri.TryCreate(options.Endpoint?.Trim(), UriKind.Absolute, out Uri? endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Azure Artifact Signing Endpoint must be an absolute HTTPS URL.");
        if (string.IsNullOrWhiteSpace(options.AccountName))
            throw new ArgumentException("Azure Artifact Signing AccountName is required.");
        if (string.IsNullOrWhiteSpace(options.CertificateProfileName))
            throw new ArgumentException("Azure Artifact Signing CertificateProfileName is required.");
        if (string.IsNullOrWhiteSpace(options.DlibPath))
            throw new ArgumentException("Azure Artifact Signing DlibPath is required.");
        if (string.IsNullOrWhiteSpace(sign.SubjectName))
            throw new ArgumentException("Azure Artifact Signing requires the expected certificate SubjectName for release verification.");
    }

    private static string? ResolveAzureArtifactSigningDlibPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string raw = path!.Trim().Trim('"');
        if (File.Exists(raw)) return Path.GetFullPath(raw);
        return ResolveOnPath(raw);
    }

    private static void WriteAzureArtifactSigningMetadata(
        string path,
        DotNetPublishAzureArtifactSigningOptions options)
    {
        string endpoint = new Uri(options.Endpoint!.Trim(), UriKind.Absolute).AbsoluteUri;
        var metadata = new Dictionary<string, object?>
        {
            ["Endpoint"] = endpoint,
            ["CodeSigningAccountName"] = options.AccountName!.Trim(),
            ["CertificateProfileName"] = options.CertificateProfileName!.Trim()
        };
        if (!string.IsNullOrWhiteSpace(options.CorrelationId))
            metadata["CorrelationId"] = options.CorrelationId!.Trim();
        string[] excluded = (options.ExcludeCredentials ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (excluded.Length > 0)
            metadata["ExcludeCredentials"] = excluded;
        File.WriteAllText(path, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    private void DeleteAzureArtifactSigningMetadata(string? root, Exception? activeFailure)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception cleanupFailure) when (activeFailure is not null)
        {
            _logger.Warn($"Azure Artifact Signing metadata cleanup failed after signing failed: {cleanupFailure.Message}");
        }
        catch (Exception cleanupFailure)
        {
            throw new IOException("Azure Artifact Signing metadata cleanup failed.", cleanupFailure);
        }
    }

    private static bool SignatureMatchesPublisher(string filePath, DotNetPublishSignOptions sign)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;
        if (string.IsNullOrWhiteSpace(sign.Thumbprint) && string.IsNullOrWhiteSpace(sign.SubjectName))
            return false;

        try
        {
            DotNetPublishReleaseArtifactVerifier.AuthenticodeResult signature =
                DotNetPublishReleaseArtifactVerifier.VerifyAuthenticode(filePath);
            if (!signature.IsValid)
                return false;
            if (!string.IsNullOrWhiteSpace(sign.Thumbprint))
            {
                return string.Equals(
                    DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(signature.Thumbprint),
                    DotNetPublishReleaseArtifactVerifier.NormalizeThumbprint(sign.Thumbprint),
                    StringComparison.OrdinalIgnoreCase);
            }

            return DotNetPublishReleaseArtifactVerifier.CertificateSubjectsEqual(
                signature.Subject,
                sign.SubjectName!);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private ProcessRunResult RunSigningTool(
        string signToolPath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        var result = _processRunner.RunAsync(
                new ProcessRunRequest(
                    signToolPath,
                    workingDirectory,
                    arguments,
                    timeout),
                _cancellationToken.Value)
            .GetAwaiter()
            .GetResult();
        _cancellationToken.Value.ThrowIfCancellationRequested();
        return result;
    }

    private void HandlePolicy(DotNetPublishPolicyMode policy, string message)
    {
        switch (policy)
        {
            case DotNetPublishPolicyMode.Fail:
                throw new InvalidOperationException(message);
            case DotNetPublishPolicyMode.Skip:
                if (_logger.IsVerbose) _logger.Verbose($"{message} (policy=Skip)");
                break;
            case DotNetPublishPolicyMode.Warn:
            default:
                _logger.Warn(message);
                break;
        }
    }

    internal static bool IsWindows()
    {
#if NET472
        return true;
#else
        return OperatingSystem.IsWindows();
#endif
    }

    private static string? ResolveSignToolPath(string? toolPath)
    {
        if (!string.IsNullOrWhiteSpace(toolPath))
        {
            var raw = toolPath!.Trim().Trim('\"');
            if (File.Exists(raw)) return Path.GetFullPath(raw);

            var onPath = ResolveOnPath(raw);
            if (!string.IsNullOrWhiteSpace(onPath)) return onPath;
        }

        try
        {
            var kitsRoot = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (string.IsNullOrWhiteSpace(kitsRoot)) return null;
            var baseDir = Path.Combine(kitsRoot, "Windows Kits", "10", "bin");
            if (!Directory.Exists(baseDir)) return null;

            var versions = Directory.EnumerateDirectories(baseDir)
                .Select(d => new DirectoryInfo(d))
                .OrderByDescending(d => d.Name)
                .ToArray();

            foreach (var ver in versions)
            {
                foreach (var arch in new[] { "x64", "x86" })
                {
                    var candidate = Path.Combine(ver.FullName, arch, "signtool.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static string? ResolveOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore */ }
        }
        return null;
    }

    private static void DirectoryCopy(string sourceDir, string destDir)
    {
        var source = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var dest = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        Directory.CreateDirectory(dest);

        var sourcePrefix = source + Path.DirectorySeparatorChar;
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(dir);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            var rel = full.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(sourcePrefix.Length)
                : Path.GetFileName(full) ?? full;

            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(full, target, overwrite: true);
        }
    }

    internal static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var t = template ?? string.Empty;
        foreach (var kv in tokens)
            t = ReplaceOrdinalIgnoreCase(t, "{" + kv.Key + "}", kv.Value ?? string.Empty);
        return t;
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        if (string.IsNullOrEmpty(oldValue)) return input;

        var startIndex = 0;
        var idx = input.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return input;

        var sb = new StringBuilder(input.Length);
        while (idx >= 0)
        {
            sb.Append(input, startIndex, idx - startIndex);
            sb.Append(newValue ?? string.Empty);
            startIndex = idx + oldValue.Length;
            idx = input.IndexOf(oldValue, startIndex, StringComparison.OrdinalIgnoreCase);
        }

        sb.Append(input, startIndex, input.Length - startIndex);
        return sb.ToString();
    }

    private void RunDotnet(
        string workingDir,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        var result = RunCancellableProcess("dotnet", workingDir, args, environmentVariables);
        if (result.ExitCode != 0)
        {
            var stderr = (result.StdErr ?? string.Empty).TrimEnd();
            var stdout = (result.StdOut ?? string.Empty).TrimEnd();

            var stderrTail = TailLines(stderr, maxLines: 80, maxChars: 8000);
            var stdoutTail = TailLines(stdout, maxLines: 80, maxChars: 8000);

            var msg = ExtractBestFailureLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
            if (string.IsNullOrWhiteSpace(msg)) msg = "dotnet failed.";

            throw new DotNetPublishCommandException(
                message: msg,
                fileName: "dotnet",
                workingDirectory: string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
                args: args,
                exitCode: result.ExitCode,
                stdOut: stdout,
                stdErr: stderr);
        }

        if (_logger.IsVerbose)
        {
            if (!string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.TrimEnd());
            if (!string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.TrimEnd());
        }
    }

    private (int ExitCode, string StdOut, string StdErr) RunCancellableProcess(
        string fileName,
        string workingDir,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentVariables)
    {
        var result = _processRunner.RunAsync(
                new ProcessRunRequest(
                    fileName,
                    string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
                    args,
                    Timeout.InfiniteTimeSpan,
                    environmentVariables),
                _cancellationToken.Value)
            .GetAwaiter()
            .GetResult();
        _cancellationToken.Value.ThrowIfCancellationRequested();
        return (result.ExitCode, result.StdOut, result.StdErr);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(
        string fileName,
        string workingDir,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentVariables = null)
    {
        var result = RunProcessCore(fileName, workingDir, args, timeout: null, environmentVariables);
        return (result.ExitCode, result.StdOut, result.StdErr);
    }

    private static (int ExitCode, string StdOut, string StdErr, bool TimedOut) RunProcessWithTimeout(
        string fileName,
        string workingDir,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        => RunProcessCore(
            fileName,
            workingDir,
            args,
            timeout,
            environmentVariables: null,
            cancellationToken);

    private static (int ExitCode, string StdOut, string StdErr, bool TimedOut) RunProcessCore(
        string fileName,
        string workingDir,
        IReadOnlyList<string> args,
        TimeSpan? timeout,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        if (environmentVariables is not null)
        {
            foreach (var variable in environmentVariables)
            {
                if (string.IsNullOrWhiteSpace(variable.Key))
                    continue;

                if (variable.Value is null)
                    psi.EnvironmentVariables.Remove(variable.Key);
                else
                    psi.EnvironmentVariables[variable.Key] = variable.Value;
            }
        }

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var a in args) psi.ArgumentList.Add(a);
#endif

        using var p = Process.Start(psi)!;
        using var cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            p);
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero && timeout.Value != Timeout.InfiniteTimeSpan)
        {
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();
            using var stdoutDone = new ManualResetEventSlim(false);
            using var stderrDone = new ManualResetEventSlim(false);
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stdoutDone.Set();
                else
                    stdoutBuilder.AppendLine(e.Data);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stderrDone.Set();
                else
                    stderrBuilder.AppendLine(e.Data);
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            var timeoutMs = ToTimeoutMilliseconds(timeout.Value);
            if (!p.WaitForExit(timeoutMs))
            {
                TryKillProcessTree(p);
                var timeoutMessage = $"Process timed out after {Math.Ceiling(timeout.Value.TotalSeconds)} second(s).";
                if (stderrBuilder.Length > 0)
                    stderrBuilder.AppendLine();
                stderrBuilder.Append(timeoutMessage);
                return (-1, stdoutBuilder.ToString(), stderrBuilder.ToString(), true);
            }

            p.WaitForExit();
            stdoutDone.Wait(TimeSpan.FromSeconds(5));
            stderrDone.Wait(TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            return (p.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString(), false);
        }

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        return (p.ExitCode, stdout, stderr, false);
    }

    private static int ToTimeoutMilliseconds(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds;
        if (milliseconds >= int.MaxValue) return int.MaxValue;
        return Math.Max(1, (int)Math.Ceiling(milliseconds));
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
#if NET472
            process.Kill();
#else
            process.Kill(entireProcessTree: true);
#endif
        }
        catch
        {
            // best effort
        }
        try
        {
            process.WaitForExit(5000);
        }
        catch
        {
            // best effort
        }
    }

#if NET472
    private static string BuildWindowsArgumentString(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(EscapeWindowsArgument));

    // Based on .NET's internal ProcessStartInfo quoting behavior for Windows CreateProcess.
    private static string EscapeWindowsArgument(string arg)
    {
        if (arg is null) return "\"\"";
        if (arg.Length == 0) return "\"\"";

        bool needsQuotes = arg.Any(ch => char.IsWhiteSpace(ch) || ch == '"');
        if (!needsQuotes) return arg;

        var sb = new StringBuilder();
        sb.Append('"');

        int backslashCount = 0;
        foreach (var ch in arg)
        {
            if (ch == '\\')
            {
                backslashCount++;
                continue;
            }

            if (ch == '"')
            {
                sb.Append('\\', backslashCount * 2 + 1);
                sb.Append('"');
                backslashCount = 0;
                continue;
            }

            if (backslashCount > 0)
            {
                sb.Append('\\', backslashCount);
                backslashCount = 0;
            }

            sb.Append(ch);
        }

        if (backslashCount > 0)
            sb.Append('\\', backslashCount * 2);

        sb.Append('"');
        return sb.ToString();
    }
#endif

    private static string? TailLines(string? text, int maxLines, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = (text ?? string.Empty).Replace("\r\n", "\n");
        var end = normalized.Length;
        if (end == 0) return null;

        maxLines = Math.Max(1, maxLines);
        maxChars = Math.Max(1, maxChars);

        int lines = 0;
        int start = 0;
        for (int i = end - 1; i >= 0; i--)
        {
            if (normalized[i] == '\n')
            {
                lines++;
                if (lines > maxLines)
                {
                    start = i + 1;
                    break;
                }
            }
        }

        var tail = normalized.Substring(start).TrimEnd();
        if (tail.Length > maxChars)
            tail = tail.Substring(tail.Length - maxChars);
        return tail;
    }

}
