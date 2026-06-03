using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <content>
/// Validation helpers for isolated module import profiles.
/// </content>
public sealed partial class IsolatedModuleImportService
{
    /// <summary>Validates a module isolation profile without copying or importing the module.</summary>
    public IsolatedModuleProfileValidationResult Validate(IsolatedModuleImportRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var result = new IsolatedModuleProfileValidationResult();

        try
        {
            EnsureSupportedRuntime();
        }
        catch (Exception ex)
        {
            AddIssue(result, "Runtime", ex.Message);
            return result;
        }

        ModuleIsolationProfile profile;
        try
        {
            profile = _profiles.Resolve(request.ProfileName);
            PopulateProfile(result, profile);
        }
        catch (Exception ex)
        {
            AddIssue(result, "Profile", ex.Message);
            return result;
        }

        try
        {
            var source = ResolveModuleSource(request, profile);
            PopulateSource(result, source);
            result.ResolvedVersion = ReadManifestVersionSafe(source.ManifestPath);

            foreach (var issue in ValidateProfileLayout(profile, source, result.Paths))
                result.Issues.Add(issue);
        }
        catch (Exception ex)
        {
            AddIssue(result, "ModuleResolution", ex.Message);
        }

        return result;
    }

    private static void PopulateProfile(IsolatedModuleProfileValidationResult result, ModuleIsolationProfile profile)
    {
        result.ProfileName = profile.Name;
        result.ModuleName = profile.ModuleName;
        result.ContextName = profile.ContextName;
        result.MinimumVersion = profile.MinimumVersion;
    }

    private static void PopulateSource(IsolatedModuleProfileValidationResult result, ResolvedModuleSource source)
    {
        result.SourceModuleBase = source.ModuleBase;
        result.ManifestPath = source.ManifestPath;
    }

    private static Version? ReadManifestVersionSafe(string manifestPath)
    {
        try
        {
            return File.Exists(manifestPath) ? ReadManifestVersion(manifestPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<IsolatedModuleProfileValidationIssue> ValidateProfileLayout(ModuleIsolationProfile profile, ResolvedModuleSource source)
        => ValidateProfileLayout(profile, source, paths: null);

    private static List<IsolatedModuleProfileValidationIssue> ValidateProfileLayout(
        ModuleIsolationProfile profile,
        ResolvedModuleSource source,
        List<IsolatedModuleProfileValidationPath>? paths)
    {
        var issues = new List<IsolatedModuleProfileValidationIssue>();
        var scriptDirectory = Path.Combine(
            source.ModuleBase,
            Path.GetDirectoryName(NormalizeRelativePath(profile.ScriptRelativePath)) ?? string.Empty);

        AddRequiredRelativeFile(issues, paths, source.ModuleBase, "SourceScript", profile.ScriptRelativePath);

        if (!string.IsNullOrWhiteSpace(profile.ManifestRelativePath))
        {
            AddRequiredFile(issues, paths, "Manifest", source.ManifestPath, profile.ManifestRelativePath);
            if (File.Exists(source.ManifestPath))
            {
                try
                {
                    var sourceManifest = File.ReadAllText(source.ManifestPath);
                    _ = PatchManifestText(sourceManifest, source.ManifestPath, "./" + profile.IsolatedScriptName, profile.RemoveManifestNestedModules);
                }
                catch (Exception ex)
                {
                    AddIssue(issues, "Manifest", ex.Message, source.ManifestPath, profile.ManifestRelativePath);
                }
            }
        }

        foreach (var relativePath in profile.DependencyAssemblyImports)
            AddRequiredRelativeFile(issues, paths, scriptDirectory, "DependencyAssembly", relativePath);

        foreach (var relativePath in profile.BinaryImports)
            AddRequiredRelativeFile(issues, paths, scriptDirectory, "BinaryModule", relativePath);

        foreach (var relativePath in profile.CopiedScriptBinaryImports)
            AddRequiredRelativeFile(issues, paths, source.ModuleBase, "CopiedScriptBinaryImport", relativePath);

        foreach (var relativePath in profile.RequiredFiles)
            AddRequiredRelativeFile(issues, paths, source.ModuleBase, "RequiredFile", relativePath);

        return issues;
    }

    private static void AddRequiredRelativeFile(
        List<IsolatedModuleProfileValidationIssue> issues,
        List<IsolatedModuleProfileValidationPath>? paths,
        string moduleBase,
        string category,
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return;

        var fullPath = Path.Combine(moduleBase, NormalizeRelativePath(relativePath));
        AddRequiredFile(issues, paths, category, fullPath, relativePath);
    }

    private static void AddRequiredFile(
        List<IsolatedModuleProfileValidationIssue> issues,
        List<IsolatedModuleProfileValidationPath>? paths,
        string category,
        string fullPath,
        string? relativePath)
    {
        var exists = File.Exists(fullPath);
        paths?.Add(new IsolatedModuleProfileValidationPath
        {
            Category = category,
            RelativePath = relativePath,
            Path = fullPath,
            Exists = exists
        });

        if (!exists)
            AddIssue(issues, category, $"Required profile file was not found: {fullPath}", fullPath, relativePath);
    }

    private static void AddIssue(IsolatedModuleProfileValidationResult result, string category, string message, string? path = null, string? relativePath = null)
        => result.Issues.Add(CreateIssue(category, message, path, relativePath));

    private static void AddIssue(List<IsolatedModuleProfileValidationIssue> issues, string category, string message, string? path = null, string? relativePath = null)
        => issues.Add(CreateIssue(category, message, path, relativePath));

    private static IsolatedModuleProfileValidationIssue CreateIssue(string category, string message, string? path, string? relativePath)
        => new()
        {
            Severity = "Error",
            Category = category,
            Message = message,
            Path = path,
            RelativePath = relativePath
        };

    private static string BuildValidationFailureMessage(ModuleIsolationProfile profile, IReadOnlyCollection<IsolatedModuleProfileValidationIssue> issues)
    {
        var details = string.Join(
            Environment.NewLine,
            issues
                .Where(static issue => string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase))
                .Select(static issue => "- " + issue.Category + ": " + issue.Message));

        return $"Profile '{profile.Name}' failed preflight validation before isolated import.{Environment.NewLine}{details}";
    }
}
