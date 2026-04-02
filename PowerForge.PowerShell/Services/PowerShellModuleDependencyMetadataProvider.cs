using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PowerForge;

internal sealed class PowerShellModuleDependencyMetadataProvider : IModuleDependencyMetadataProvider
{
    private readonly IPowerShellRunner _powerShellRunner;
    private readonly ILogger _logger;

    internal PowerShellModuleDependencyMetadataProvider(IPowerShellRunner powerShellRunner, ILogger logger)
    {
        _powerShellRunner = powerShellRunner;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, InstalledModuleMetadata> GetLatestInstalledModules(IReadOnlyList<string> names)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0)
            return new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);

        var script = EmbeddedScripts.Load("Scripts/ModulePipeline/Get-InstalledModuleInfo.ps1");
        var args = new List<string>(1) { EncodeLines(list) };
        var result = RunScript(script, args, TimeSpan.FromMinutes(2));

        if (result.ExitCode != 0)
        {
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdOut)) _logger.Verbose(result.StdOut.Trim());
            if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(result.StdErr)) _logger.Verbose(result.StdErr.Trim());
            return new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, InstalledModuleMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODINFO::ITEM::", StringComparison.Ordinal))
                continue;

            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 6)
                continue;

            var name = Decode(parts[2]);
            var version = EmptyToNull(Decode(parts[3]));
            var guid = EmptyToNull(Decode(parts[4]));
            var moduleBasePath = EmptyToNull(Decode(parts[5]));
            if (string.IsNullOrWhiteSpace(name))
                continue;

            map[name] = new InstalledModuleMetadata(name, version, guid, moduleBasePath);
        }

        foreach (var name in list)
        {
            if (!map.ContainsKey(name))
                map[name] = new InstalledModuleMetadata(name, null, null, null);
        }

        return map;
    }

    public IReadOnlyDictionary<string, (string? Version, string? Guid)> ResolveLatestOnlineVersions(
        IReadOnlyCollection<string> names,
        string? repository,
        RepositoryCredential? credential,
        bool prerelease)
    {
        var list = (names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (list.Length == 0)
            return new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        var repositories = ParseRepositoryList(repository);
        IReadOnlyList<PSResourceInfo> items = Array.Empty<PSResourceInfo>();
        var resolved = new Dictionary<string, (string? Version, string? Guid)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psResourceGet = new PSResourceGetClient(_powerShellRunner, _logger);
            var options = new PSResourceFindOptions(list, version: null, prerelease: prerelease, repositories: repositories, credential: credential);
            items = psResourceGet.Find(options, timeout: TimeSpan.FromMinutes(2));
            resolved = RequiredModuleResolutionEngine.SelectLatestVersions(items, prerelease);
            if (resolved.Count > 0)
                return resolved;
        }
        catch (PowerShellToolNotAvailableException)
        {
            // fall back to PowerShellGet
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-PSResource failed while resolving RequiredModules. {ex.Message}");
        }

        try
        {
            var powerShellGet = new PowerShellGetClient(_powerShellRunner, _logger);
            var useRepositories = repositories.Length == 0 ? new[] { "PSGallery" } : repositories;
            var options = new PowerShellGetFindOptions(list, prerelease: prerelease, repositories: useRepositories, credential: credential);
            items = powerShellGet.Find(options, timeout: TimeSpan.FromMinutes(2));
            resolved = RequiredModuleResolutionEngine.SelectLatestVersions(items, prerelease);
        }
        catch (PowerShellToolNotAvailableException ex)
        {
            _logger.Warn($"PowerShellGet not available for online resolution. {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Find-Module failed while resolving RequiredModules. {ex.Message}");
        }

        return resolved;
    }

    private PowerShellRunResult RunScript(string scriptText, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var scriptPath = WriteTempScript(scriptText);
        try
        {
            return _powerShellRunner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* best effort */ }
        }
    }

    private static string WriteTempScript(string scriptText)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulepipeline");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulepipeline_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return scriptPath;
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string EncodeLines(IEnumerable<string> lines)
    {
        var joined = string.Join("\n", lines ?? Array.Empty<string>());
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(joined));
    }

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string[] ParseRepositoryList(string? repository)
    {
        var repoText = repository ?? string.Empty;
        if (string.IsNullOrWhiteSpace(repoText))
            return Array.Empty<string>();

        return repoText
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static repo => repo.Trim())
            .Where(static repo => !string.IsNullOrWhiteSpace(repo))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
