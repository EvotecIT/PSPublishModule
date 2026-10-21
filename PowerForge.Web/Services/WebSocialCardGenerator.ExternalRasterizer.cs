using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PowerForge.Web;

internal static partial class WebSocialCardGenerator
{
    private static byte[]? TryRenderPngWithExternalImageMagick(string svg, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(svg))
            return null;

        var command = ResolveExternalImageMagickCommand();
        if (command is null)
            return null;

        var tempRoot = Path.Combine(Path.GetTempPath(), "powerforge-social-card-" + Guid.NewGuid().ToString("N"));
        var svgPath = Path.Combine(tempRoot, "card.svg");
        var pngPath = Path.Combine(tempRoot, "card.png");
        var mvgPath = Path.Combine(tempRoot, "card.mvg");

        try
        {
            Directory.CreateDirectory(tempRoot);
            var legacy = RequiresLegacyMvgCompatibility(command);
            if (legacy)
            {
                var document = XDocument.Parse(svg);
                foreach (var family in document.Descendants().Attributes("font-family"))
                    family.Value = NormalizeFontFamilyForRaster(family.Value, family.Value);
                svg = document.ToString(SaveOptions.DisableFormatting);
            }
            File.WriteAllText(svgPath, svg, Encoding.UTF8);
            var input = svgPath;
            if (legacy)
            {
                if (!RunExternalRasterizer(command, new[] { svgPath, "MVG:" + mvgPath }))
                    return null;
                // ImageMagick 6's SVG coder serializes quoted gradient identifiers which its
                // own MVG reader cannot resolve. Its SVG compliance mode also discards opacity.
                // Keep the native SVG parser, then repair only those generated draw directives.
                var mvg = NormalizeLegacyMvg(File.ReadAllText(mvgPath));
                File.WriteAllText(mvgPath, mvg, new UTF8Encoding(false));
                input = "MVG:" + mvgPath;
            }
            var arguments = new[] { "-background", "none", "-size", $"{width}x{height}", input, $"PNG32:{pngPath}" };
            return RunExternalRasterizer(command, arguments) && File.Exists(pngPath) ? File.ReadAllBytes(pngPath) : null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Social card external ImageMagick render failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch { /* best effort cleanup of this render only */ }
        }
    }

    private static string NormalizeLegacyMvg(string mvg)
    {
        mvg = Regex.Replace(mvg, @"(?m)^compliance ""SVG""\r?$", string.Empty, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        mvg = Regex.Replace(mvg, @"(?m)^push gradient ""([A-Za-z0-9_-]+)"" ", "push gradient $1 ", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return Regex.Replace(mvg, @"(?m)^(fill|stroke) ""url\(#([A-Za-z0-9_-]+)\)""\r?$", "$1 url(#$2)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> LegacyRasterizerCommands = new(StringComparer.Ordinal);

    private static bool RequiresLegacyMvgCompatibility(ExternalImageMagickCommand command)
        => LegacyRasterizerCommands.GetOrAdd(command.Executable, executable =>
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "-version")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            });
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5_000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }
            _ = error.GetAwaiter().GetResult();
            return process.ExitCode == 0 && output.GetAwaiter().GetResult().Contains("ImageMagick 6.", StringComparison.Ordinal);
        });

    private static bool RunExternalRasterizer(ExternalImageMagickCommand command, IReadOnlyList<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command.Executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            var errorTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(30_000);
            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup for a timed-out renderer process.
                }

                Trace.TraceWarning("Social card external ImageMagick render timed out.");
                return false;
            }

            if (process.ExitCode != 0)
            {
                var error = errorTask.GetAwaiter().GetResult().Trim();
                if (!string.IsNullOrWhiteSpace(error))
                    Trace.TraceWarning($"Social card external ImageMagick render failed: {error}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Social card external ImageMagick render failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static ExternalImageMagickCommand? ResolveExternalImageMagickCommand()
    {
        if (OperatingSystem.IsWindows())
            return null;

        var magick = FindExecutableOnPath("magick");
        if (!string.IsNullOrWhiteSpace(magick))
            return new ExternalImageMagickCommand(magick);

        var convert = FindExecutableOnPath("convert");
        if (!string.IsNullOrWhiteSpace(convert))
            return new ExternalImageMagickCommand(convert);

        return null;
    }

    private static string? FindExecutableOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate) && IsExecutable(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return true;

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private sealed record ExternalImageMagickCommand(string Executable);
}
