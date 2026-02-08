using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private void TrySignOutput(string outputDir, DotNetPublishSignOptions sign)
    {
        if (sign is null || !sign.Enabled) return;
        if (!IsWindows())
        {
            _logger.Warn("Signing requested but current OS is not Windows. Skipping signing.");
            return;
        }

        var signTool = ResolveSignToolPath(sign.ToolPath);
        if (string.IsNullOrWhiteSpace(signTool))
        {
            _logger.Warn("Signing requested but signtool.exe was not found. Skipping signing.");
            return;
        }

        var signToolPath = signTool!;
        if (!File.Exists(signToolPath))
        {
            _logger.Warn($"Signing requested but signtool.exe was not found: {signToolPath}. Skipping signing.");
            return;
        }

        var targets = new List<string>();
        try
        {
            targets.AddRange(Directory.EnumerateFiles(outputDir, "*.exe", SearchOption.AllDirectories));
            targets.AddRange(Directory.EnumerateFiles(outputDir, "*.dll", SearchOption.AllDirectories));
        }
        catch
        {
            // ignore
        }

        if (targets.Count == 0) return;

        _logger.Info($"Signing {targets.Count} file(s) using {Path.GetFileName(signToolPath)}");
        foreach (var file in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var args = new List<string> { "sign", "/fd", "SHA256" };
            if (!string.IsNullOrWhiteSpace(sign.TimestampUrl))
                args.AddRange(new[] { "/tr", sign.TimestampUrl!, "/td", "SHA256" });
            if (!string.IsNullOrWhiteSpace(sign.Description))
                args.AddRange(new[] { "/d", sign.Description! });
            if (!string.IsNullOrWhiteSpace(sign.Url))
                args.AddRange(new[] { "/du", sign.Url! });

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

            args.Add(file);
            var res = RunProcess(signToolPath, outputDir, args);
            if (res.ExitCode != 0)
                _logger.Warn($"Signing failed for '{file}'. {res.StdErr}".Trim());
        }
    }

    private static bool IsWindows()
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

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> tokens)
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

    private void RunDotnet(string workingDir, IReadOnlyList<string> args)
    {
        var result = RunProcess("dotnet", workingDir, args);
        if (result.ExitCode != 0)
        {
            var stderr = (result.StdErr ?? string.Empty).TrimEnd();
            var stdout = (result.StdOut ?? string.Empty).TrimEnd();

            var stderrTail = TailLines(stderr, maxLines: 80, maxChars: 8000);
            var stdoutTail = TailLines(stdout, maxLines: 80, maxChars: 8000);

            var msg = ExtractLastNonEmptyLine(!string.IsNullOrWhiteSpace(stderrTail) ? stderrTail : stdoutTail);
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

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string workingDir, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? Environment.CurrentDirectory : workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var a in args) psi.ArgumentList.Add(a);
#endif

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
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
