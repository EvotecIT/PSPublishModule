using System;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Installs (copies) module documentation to a destination according to layout
/// (Direct/Module/ModuleAndVersion) and overwrite/merge options.
/// Handles Internals content and selected root files (README/CHANGELOG/LICENSE).
/// </summary>
internal sealed class DocumentationInstaller
{
    private readonly Action<string>? _verbose;
    private readonly Action<string>? _introWriter;

    public DocumentationInstaller(Action<string>? verbose = null, Action<string>? introWriter = null)
    {
        _verbose = verbose;
        _introWriter = introWriter;
    }

    /// <summary>
    /// Computes the destination path for the given module and layout.
    /// </summary>
    public string PlanDestination(string moduleName, string moduleVersion, string path, DocumentationLayout layout)
    {
        return layout switch
        {
            DocumentationLayout.Direct => path,
            DocumentationLayout.Module => Path.Combine(path, moduleName),
            DocumentationLayout.ModuleAndVersion => Path.Combine(Path.Combine(path, moduleName), moduleVersion),
            _ => path
        };
    }

    /// <summary>
    /// Copies Internals and selected root files to the destination. Returns the destination path.
    /// </summary>
    public string Install(string moduleBase, string moduleName, string moduleVersion, string dest, OnExistsOption onExists, bool force, bool open, bool noIntro, object? delivery = null)
    {
        var root = moduleBase;
        // Resolve Internals path via manifest when available
        string? internals = null;
        var options = new DeliveryOptions();
        if (delivery != null)
        {
            var internalsRel = GetDeliveryString(delivery, "InternalsPath") ?? options.InternalsPath ?? "Internals";
            options.InternalsPath = internalsRel;
            var cand = Path.Combine(root, internalsRel);
            if (Directory.Exists(cand)) internals = cand;
        }

        if (internals == null)
        {
            var cand = Path.Combine(root, "Internals");
            if (Directory.Exists(cand)) internals = cand;
        }
        if (Directory.Exists(dest))
        {
            switch (onExists)
            {
                case OnExistsOption.Skip:
                    return dest;
                case OnExistsOption.Stop:
                    throw new IOException($"Destination '{dest}' already exists.");
                case OnExistsOption.Overwrite:
                    PrepareDirectoryDeleteTarget(dest, force);
                    Directory.Delete(dest, true);
                    break;
                case OnExistsOption.Merge:
                    // keep existing, merge below
                    break;
            }
        }

        PathHelper.EnsureDirectory(dest);
        if (internals != null)
        {
            CopyTree(internals, dest, overwrite: force);
        }
        else
        {
            _verbose?.Invoke($"Internals path '{options.InternalsPath}' not found under '{root}'; copying root documentation only.");
        }

        // Copy selected root files
        TryCopyIfMatch(root, "README*", dest, force);
        TryCopyIfMatch(root, "CHANGELOG*", dest, force);
        // Normalize license name if present
        var lic = new DirectoryInfo(root).GetFiles("LICENSE*").FirstOrDefault();
        if (lic != null)
        {
            var licTarget = Path.Combine(dest, "license.txt");
            if (!(File.Exists(licTarget) && !force))
            {
                PrepareOverwriteTarget(licTarget, force);
                lic.CopyTo(licTarget, overwrite: true);
            }
        }

        if (!noIntro)
        {
            WriteIntro(root, delivery);
        }

        if (open)
        {
            try
            {
                var readme = new DirectoryInfo(dest).GetFiles("README*").FirstOrDefault();
                if (readme != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(readme.FullName) { UseShellExecute = true });
                else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
            }
            catch { }
        }

        return dest;
    }

    internal static string[] GetIntroLinesForTesting(string moduleRoot, object? delivery)
        => BuildIntroLines(moduleRoot, delivery).ToArray();

    private void WriteIntro(string moduleRoot, object? delivery)
    {
        foreach (var line in BuildIntroLines(moduleRoot, delivery))
        {
            try { _introWriter?.Invoke(line); } catch { }
        }
    }

    private static string[] BuildIntroLines(string moduleRoot, object? delivery)
    {
        var introText = GetDeliveryStringArray(delivery, "IntroText");
        if (introText.Length > 0)
            return introText;

        var introFile = GetDeliveryString(delivery, "IntroFile");
        if (string.IsNullOrWhiteSpace(introFile))
            return Array.Empty<string>();

        var path = Path.IsPathRooted(introFile!)
            ? introFile!
            : Path.Combine(moduleRoot, introFile!);

        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void CopyTree(string source, string dest, bool overwrite)
    {
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = dir.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetDir = Path.Combine(dest, rel);
            PathHelper.EnsureDirectory(targetDir);
        }
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && !overwrite)
            {
                // Merge semantics: keep existing file unless Force/overwrite specified
                continue;
            }
            PrepareOverwriteTarget(target, overwrite);
            File.Copy(file, target, true);
        }
    }

    private static void TryCopyIfMatch(string root, string pattern, string dest, bool force)
    {
        var di = new DirectoryInfo(root);
        foreach (var f in di.GetFiles(pattern))
        {
            var target = Path.Combine(dest, f.Name);
            if (File.Exists(target) && !force) continue; // keep existing unless -Force
            PrepareOverwriteTarget(target, force);
            f.CopyTo(target, overwrite: true);
        }
    }

    private static void PrepareOverwriteTarget(string target, bool overwrite)
    {
        if (!overwrite || !File.Exists(target)) return;

        try
        {
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
            // Let the following copy operation report the real filesystem failure.
        }
    }

    private static void PrepareDirectoryDeleteTarget(string target, bool force)
    {
        if (!force || !Directory.Exists(target)) return;

        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(target, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                }
                catch
                {
                    // Directory.Delete will report the real failure if the attribute remains a blocker.
                }
            }

            File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
        }
        catch
        {
            // Let the following delete operation report the real filesystem failure.
        }
    }

    private static string? GetDeliveryString(object? delivery, string name)
    {
        if (delivery == null || string.IsNullOrWhiteSpace(name)) return null;

        if (delivery is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null && string.Equals(entry.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value?.ToString();
            }
        }

        var reflected = delivery.GetType().GetProperty(name);
        return reflected?.GetValue(delivery)?.ToString();
    }

    private static string[] GetDeliveryStringArray(object? delivery, string name)
    {
        var value = GetDeliveryValue(delivery, name);
        if (value is null)
            return Array.Empty<string>();

        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text };

        if (value is System.Collections.IEnumerable enumerable)
        {
            return enumerable
                .Cast<object?>()
                .Select(item => item?.ToString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray();
        }

        var scalar = value.ToString();
        return string.IsNullOrWhiteSpace(scalar) ? Array.Empty<string>() : new[] { scalar! };
    }

    private static object? GetDeliveryValue(object? delivery, string name)
    {
        if (delivery == null || string.IsNullOrWhiteSpace(name)) return null;

        if (delivery is System.Collections.IDictionary dictionary)
        {
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key != null && string.Equals(entry.Key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        var reflected = delivery.GetType().GetProperty(name);
        return reflected?.GetValue(delivery);
    }
}
