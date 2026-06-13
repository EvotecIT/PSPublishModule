// ReSharper disable All
using System;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Resolves module base paths (root and Internals) and maps delivery options from the manifest.
/// Provides helpers to locate standard documents (README/CHANGELOG/LICENSE/UPGRADE).
/// </summary>
internal sealed class DocumentationFinder
{
    /// <summary>
    /// Resolves a file by <see cref="DocumentKind"/> from root or Internals depending on preference.
    /// </summary>
    public FileInfo? ResolveDocument((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, DocumentKind kind, bool preferInternals)
    {
        string pattern = kind switch
        {
            DocumentKind.Readme => "README*",
            DocumentKind.Changelog => "CHANGELOG*",
            DocumentKind.License => "LICENSE*",
            DocumentKind.Upgrade => "UPGRADE*",
            _ => ""
        };

        if (string.IsNullOrEmpty(pattern)) return null;

        var root = bases.RootBase;
        var internals = bases.InternalsBase;

        FileInfo? first(DirectoryInfo d)
            => d.Exists ? d.GetFiles(pattern).OrderBy(f => f.Name.Length).FirstOrDefault() : null;

        var rootPick = first(new DirectoryInfo(root));
        var internalsPick = internals != null ? first(new DirectoryInfo(internals)) : null;

        if (preferInternals && internalsPick != null) return (FileInfo?)internalsPick;
        return (FileInfo?)(rootPick ?? internalsPick);
    }

    /// <summary>
    /// Returns all about_*.help.txt files discoverable under common locations
    /// (en-US under root/internals and any explicit Docs paths from Delivery metadata).
    /// </summary>
    public System.Collections.Generic.IEnumerable<FileInfo> ResolveAboutTopics((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, System.Collections.Generic.IEnumerable<string>? extraDocsPaths = null)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new System.Collections.Generic.List<string>
        {
            Path.Combine(bases.RootBase, "en-US"),
            bases.RootBase,
            Path.Combine(bases.RootBase, "Help", "About")
        };
        if (!string.IsNullOrWhiteSpace(bases.InternalsBase))
        {
            roots.Add(Path.Combine(bases.InternalsBase!, "en-US"));
            roots.Add(bases.InternalsBase!);
            roots.Add(Path.Combine(bases.InternalsBase!, "Help", "About"));
        }
        if (extraDocsPaths != null)
        {
            foreach (var docPath in extraDocsPaths)
            {
                if (string.IsNullOrWhiteSpace(docPath)) continue;
                var abs = Path.Combine(bases.RootBase, docPath);
                roots.Add(abs);
                roots.Add(Path.Combine(abs, "en-US"));
            }
        }

        foreach (var r in roots.Where(Directory.Exists))
        {
            foreach (var f in new DirectoryInfo(r).GetFiles("about_*.help.txt", SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(f.FullName)) yield return f;
            }
        }
    }

    /// <summary>
    /// Returns all *.Format.ps1xml files from manifest declarations or common locations.
    /// </summary>
    public System.Collections.Generic.IEnumerable<FileInfo> ResolveFormatFiles((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, System.Collections.Generic.IEnumerable<string>? manifestFormats)
        => ResolvePs1Xml(bases, manifestFormats, suffix: ".format.ps1xml");

    /// <summary>
    /// Returns all *.Types.ps1xml files from manifest declarations or common locations.
    /// </summary>
    public System.Collections.Generic.IEnumerable<FileInfo> ResolveTypesFiles((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, System.Collections.Generic.IEnumerable<string>? manifestTypes)
        => ResolvePs1Xml(bases, manifestTypes, suffix: ".types.ps1xml");

    private System.Collections.Generic.IEnumerable<FileInfo> ResolvePs1Xml((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, System.Collections.Generic.IEnumerable<string>? manifestPaths, string suffix)
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Manifest-declared absolute/relative paths first
        if (manifestPaths != null)
        {
            foreach (var p in manifestPaths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                var candidate = Path.IsPathRooted(p) ? p : Path.Combine(bases.RootBase, p);
                if (File.Exists(candidate) && candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && seen.Add(candidate))
                {
                    yield return new FileInfo(candidate);
                }
            }
        }

        // Common locations: root, root\en-US, Internals, Internals\en-US
        var roots = new System.Collections.Generic.List<string>
        {
            bases.RootBase,
            Path.Combine(bases.RootBase, "en-US")
        };
        if (!string.IsNullOrWhiteSpace(bases.InternalsBase))
        {
            roots.Add(bases.InternalsBase!);
            roots.Add(Path.Combine(bases.InternalsBase!, "en-US"));
        }

        foreach (var r in roots.Where(Directory.Exists))
        {
            foreach (var f in new DirectoryInfo(r).GetFiles("*" + suffix, SearchOption.TopDirectoryOnly))
            {
                if (seen.Add(f.FullName)) yield return f;
            }
        }
    }

    /// <summary>
    /// Finds common community health files (CONTRIBUTING, SECURITY, SUPPORT, CODE_OF_CONDUCT).
    /// </summary>
    public System.Collections.Generic.IEnumerable<FileInfo> ResolveCommunityFiles((string RootBase, string? InternalsBase, DeliveryOptions Options) bases, System.Collections.Generic.IEnumerable<string>? extraDocsPaths = null)
    {
        var names = new [] { "CONTRIBUTING", "SECURITY", "SUPPORT", "CODE_OF_CONDUCT", "CODE_OF_CODUNDUCT", "CODE-OF-CONDUCT" };
        var exts = new [] { ".md", ".markdown", ".txt" , string.Empty};
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new System.Collections.Generic.List<string> { bases.RootBase };
        if (!string.IsNullOrWhiteSpace(bases.InternalsBase)) roots.Add(bases.InternalsBase!);
        if (extraDocsPaths != null)
        {
            foreach (var docPath in extraDocsPaths)
            {
                if (string.IsNullOrWhiteSpace(docPath)) continue;
                roots.Add(Path.Combine(bases.RootBase, docPath));
            }
        }
        foreach (var r in roots.Where(Directory.Exists))
        {
            foreach (var n in names)
            {
                foreach (var ext in exts)
                {
                    var path = Path.Combine(r, n + ext);
                    if (File.Exists(path) && seen.Add(path)) yield return new FileInfo(path);
                }
            }
        }
    }
}
