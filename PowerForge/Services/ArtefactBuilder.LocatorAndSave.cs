using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private sealed class InstalledModule
    {
        public string ModuleBasePath { get; }
        public string? Version { get; }

        public InstalledModule(string moduleBasePath, string? version)
        {
            ModuleBasePath = moduleBasePath;
            Version = version;
        }
    }

    private InstalledModule? TryGetInstalledModule(
        IPowerShellRunner runner,
        string moduleName,
        string? requiredVersion,
        string? minimumVersion,
        string? maximumVersion)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        if (string.IsNullOrWhiteSpace(moduleName)) return null;

        var script = BuildFindInstalledModuleScript();
        var args = new List<string>(4)
        {
            moduleName.Trim(),
            requiredVersion ?? string.Empty,
            minimumVersion ?? string.Empty,
            maximumVersion ?? string.Empty
        };

        var result = RunScript(runner, script, args, TimeSpan.FromMinutes(2));
        if (result.ExitCode != 0)
        {
            var msg = TryExtractModuleLocatorError(result.StdOut) ?? result.StdErr;
            throw new InvalidOperationException($"Get-Module -ListAvailable failed (exit {result.ExitCode}). {msg}".Trim());
        }

        foreach (var line in SplitLines(result.StdOut))
        {
            if (!line.StartsWith("PFMODLOC::FOUND::", StringComparison.Ordinal)) continue;
            var parts = line.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 4) continue;

            var version = Decode(parts[2]);
            var path = Decode(parts[3]);
            if (string.IsNullOrWhiteSpace(path)) continue;

            var full = Path.GetFullPath(path.Trim());
            if (!Directory.Exists(full)) continue;

            var verText = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            return new InstalledModule(full, verText);
        }

        return null;
    }

    private static PowerShellRunResult RunScript(
        IPowerShellRunner runner,
        string scriptText,
        IReadOnlyList<string> args,
        TimeSpan timeout)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PowerForge", "modulelocator");
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, $"modulelocator_{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, scriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            return runner.Run(new PowerShellRunRequest(scriptPath, args, timeout, preferPwsh: true));
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { /* ignore */ }
        }
    }

    private static IEnumerable<string> SplitLines(string? text)
        => (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

    private static string Decode(string? b64)
    {
        if (string.IsNullOrWhiteSpace(b64)) return string.Empty;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return string.Empty; }
    }

    private static string? TryExtractModuleLocatorError(string stdout)
    {
        foreach (var line in SplitLines(stdout))
        {
            if (!line.StartsWith("PFMODLOC::ERROR::", StringComparison.Ordinal)) continue;
            var b64 = line.Substring("PFMODLOC::ERROR::".Length);
            var msg = Decode(b64);
            return string.IsNullOrWhiteSpace(msg) ? null : msg;
        }
        return null;
    }

    private static string BuildFindInstalledModuleScript()
    {
        return EmbeddedScripts.Load("Scripts/ModuleLocator/Find-InstalledModule.ps1");
}

    private static bool IsSecretStoreLockedMessage(string message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.IndexOf("Microsoft.PowerShell.SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Unlock-SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("A valid password is required", StringComparison.OrdinalIgnoreCase) >= 0);

    private static string SimplifyPsResourceGetFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var trimmed = message.Trim();

        // "Save-PSResource failed (exit X). <reason>" -> "<reason>"
        if (trimmed.StartsWith("Save-PSResource failed", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "). ";
            var idx = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx + marker.Length < trimmed.Length)
                return trimmed.Substring(idx + marker.Length).Trim();
        }

        return trimmed;
    }

    private IReadOnlyList<PSResourceInfo> SaveRequiredModuleWithPowerShellGet(
        PowerShellGetClient client,
        PowerShellGetSaveOptions options,
        string moduleName)
    {
        try
        {
            return client.Save(options, timeout: TimeSpan.FromMinutes(10));
        }
        catch (Exception ex)
        {
            var raw = ex.GetBaseException().Message ?? ex.Message ?? string.Empty;
            if (ShouldTryEnsurePowerShellGetDefaultRepository(raw, options.Repository))
            {
                try { client.EnsureRepositoryRegistered(PSGalleryName, PSGalleryUriV2, PSGalleryUriV2, trusted: true, timeout: TimeSpan.FromMinutes(2)); } catch { }
                try { return client.Save(options, timeout: TimeSpan.FromMinutes(10)); } catch (Exception retryEx) { ex = retryEx; raw = retryEx.GetBaseException().Message ?? retryEx.Message ?? string.Empty; }
            }
            var reason = SimplifyPowerShellGetFailureMessage(raw);
            var hint = BuildPowerShellGetRepositoryHint(raw);

            var msg = string.IsNullOrWhiteSpace(reason)
                ? $"Save-Module failed while downloading required module '{moduleName}'."
                : $"Save-Module failed while downloading required module '{moduleName}'. {reason}";

            if (!string.IsNullOrWhiteSpace(hint))
                msg = $"{msg} {hint}";

            throw new InvalidOperationException(msg, ex);
        }
    }

    private void TryEnsurePsResourceGetDefaultRepository(PSResourceGetClient client, string? repositoryName)
    {
        if (client is null) return;

        var repo = string.IsNullOrWhiteSpace(repositoryName) ? PSGalleryName : repositoryName!.Trim();
        if (!repo.Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase)) return;
        if (_ensuredPsResourceGetRepository) return;

        try
        {
            client.EnsureRepositoryRegistered(
                name: PSGalleryName,
                uri: PSGalleryUriV2,
                trusted: true,
                priority: null,
                apiVersion: RepositoryApiVersion.Auto,
                timeout: TimeSpan.FromMinutes(2));
            _ensuredPsResourceGetRepository = true;
        }
        catch (Exception ex)
        {
            if (_logger.IsVerbose)
                _logger.Verbose($"Failed to ensure PSResourceGet repository '{PSGalleryName}' is registered: {ex.Message}");
        }
    }

    private void TryEnsurePowerShellGetDefaultRepository(PowerShellGetClient client, string? repositoryName)
    {
        if (client is null) return;

        var repo = string.IsNullOrWhiteSpace(repositoryName) ? PSGalleryName : repositoryName!.Trim();
        if (!repo.Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase)) return;
        if (_ensuredPowerShellGetRepository) return;

        try
        {
            client.EnsureRepositoryRegistered(
                name: PSGalleryName,
                sourceUri: PSGalleryUriV2,
                publishUri: PSGalleryUriV2,
                trusted: true,
                timeout: TimeSpan.FromMinutes(2));
            _ensuredPowerShellGetRepository = true;
        }
        catch (Exception ex)
        {
            if (_logger.IsVerbose)
                _logger.Verbose($"Failed to ensure PowerShellGet repository '{PSGalleryName}' is registered: {ex.Message}");
        }
    }

    private static bool ShouldTryEnsurePowerShellGetDefaultRepository(string message, string? repository)
    {
        if (!string.IsNullOrWhiteSpace(repository) && !repository!.Trim().Equals(PSGalleryName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(message)) return false;

        return message.IndexOf("Try Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Unable to find module repositories", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("Unable to find repository", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string SimplifyPowerShellGetFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var trimmed = message.Trim();

        // "Save-Module failed (exit X). <reason>" -> "<reason>"
        if (trimmed.StartsWith("Save-Module failed", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "). ";
            var idx = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx + marker.Length < trimmed.Length)
                return trimmed.Substring(idx + marker.Length).Trim();
        }

        return trimmed;
    }

    private static string BuildPowerShellGetRepositoryHint(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        if (message.IndexOf("Get-PSRepository", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("No match was found for the specified search criteria", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Verify a repository is registered and reachable (Get-PSRepository). If PSGallery is missing, run Register-PSRepository -Default.";
        }

        return string.Empty;
    }

    private static string? NormalizeVersionArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

}
