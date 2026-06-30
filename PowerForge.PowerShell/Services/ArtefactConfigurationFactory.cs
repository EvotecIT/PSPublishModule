using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Creates artefact configuration segments from cmdlet input.
/// </summary>
public sealed class ArtefactConfigurationFactory
{
    private readonly ScriptDefinitionFormatterService _formatter;

    /// <summary>
    /// Creates a new factory.
    /// </summary>
    public ArtefactConfigurationFactory(ILogger logger)
    {
        _formatter = new ScriptDefinitionFormatterService(logger ?? throw new ArgumentNullException(nameof(logger)));
    }

    /// <summary>
    /// Creates a fully normalized artefact configuration segment.
    /// </summary>
    public ConfigurationArtefactSegment Create(ArtefactConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var artefact = new ConfigurationArtefactSegment
        {
            ArtefactType = request.Type,
            Configuration = new ArtefactConfiguration
            {
                RequiredModules = new ArtefactRequiredModulesConfiguration()
            }
        };

        if (request.EnableSpecified)
            artefact.Configuration.Enabled = request.Enable;

        if (request.IncludeTagNameSpecified)
            artefact.Configuration.IncludeTagName = request.IncludeTagName;

        if (!string.IsNullOrWhiteSpace(request.Path))
        {
            var path = request.Path;
            artefact.Configuration.Path = NormalizePath(path!);
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredModulesPath))
        {
            var requiredModulesPath = request.RequiredModulesPath;
            artefact.Configuration.RequiredModules.Path = NormalizePath(requiredModulesPath!);
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredModulesRepository))
        {
            var requiredModulesRepository = request.RequiredModulesRepository;
            artefact.Configuration.RequiredModules.Repository = requiredModulesRepository!.Trim();
        }

        if (request.RequiredModulesTool.HasValue)
            artefact.Configuration.RequiredModules.Tool = request.RequiredModulesTool.Value;

        if (request.RequiredModulesSource.HasValue)
            artefact.Configuration.RequiredModules.Source = request.RequiredModulesSource.Value;

        var requiredModulesSecret = ResolveSecret(
            request.RequiredModulesCredentialSecret,
            request.RequiredModulesCredentialSecretFilePath);

        if (!string.IsNullOrWhiteSpace(requiredModulesSecret) &&
            string.IsNullOrWhiteSpace(request.RequiredModulesCredentialUserName))
        {
            throw new ArgumentException(
                "RequiredModulesCredentialUserName is required when RequiredModulesCredentialSecret/RequiredModulesCredentialSecretFilePath is provided.",
                nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(request.RequiredModulesCredentialUserName) &&
            !string.IsNullOrWhiteSpace(requiredModulesSecret))
        {
            var requiredModulesCredentialUserName = request.RequiredModulesCredentialUserName;
            artefact.Configuration.RequiredModules.Credential = new RepositoryCredential
            {
                UserName = requiredModulesCredentialUserName!.Trim(),
                Secret = requiredModulesSecret
            };
        }

        if (request.AddRequiredModulesSpecified)
            artefact.Configuration.RequiredModules.Enabled = request.AddRequiredModules;

        if (request.RequiredModulesExcludeModuleName is { Length: > 0 })
        {
            artefact.Configuration.RequiredModules.ExcludeModuleName = request.RequiredModulesExcludeModuleName
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!string.IsNullOrWhiteSpace(request.ModulesPath))
        {
            var modulesPath = request.ModulesPath;
            artefact.Configuration.RequiredModules.ModulesPath = NormalizePath(modulesPath!);
            if (string.IsNullOrWhiteSpace(artefact.Configuration.RequiredModules.Path))
                artefact.Configuration.RequiredModules.Path = NormalizePath(modulesPath!);
        }

        if (request.CopyDirectories is { Length: > 0 })
            artefact.Configuration.DirectoryOutput = NormalizeMappings(request.CopyDirectories);

        if (request.CopyDirectoriesRelativeSpecified)
            artefact.Configuration.DestinationDirectoriesRelative = request.CopyDirectoriesRelative;

        if (request.CopyFiles is { Length: > 0 })
            artefact.Configuration.FilesOutput = NormalizeMappings(request.CopyFiles);

        if (request.CopyFilesRelativeSpecified)
            artefact.Configuration.DestinationFilesRelative = request.CopyFilesRelative;

        if (request.DoNotClearSpecified)
            artefact.Configuration.DoNotClear = request.DoNotClear;

        if (!string.IsNullOrWhiteSpace(request.ArtefactName))
            artefact.Configuration.ArtefactName = request.ArtefactName;

        if (!string.IsNullOrWhiteSpace(request.ScriptName))
            artefact.Configuration.ScriptName = request.ScriptName;

        artefact.Configuration.PreScriptMerge = ResolveMergeScript(request.PreScriptMergeText, request.PreScriptMergePath);
        artefact.Configuration.PostScriptMerge = ResolveMergeScript(request.PostScriptMergeText, request.PostScriptMergePath);

        if (!string.IsNullOrWhiteSpace(request.ID))
            artefact.Configuration.ID = request.ID;

        return artefact;
    }

    private string? ResolveMergeScript(string? inlineScript, string? scriptPath)
    {
        var scriptContent = !string.IsNullOrWhiteSpace(inlineScript)
            ? inlineScript
            : (!string.IsNullOrWhiteSpace(scriptPath) ? File.ReadAllText(ResolveMergeScriptPath(scriptPath!)) : null);

        if (string.IsNullOrWhiteSpace(scriptContent))
            return null;

        return _formatter.Format(scriptContent!);
    }

    private static string ResolveMergeScriptPath(string scriptPath)
    {
        var cleaned = PathValueResolver.Clean(scriptPath);
        if (Path.IsPathRooted(cleaned))
            return cleaned;

        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        if (currentDirectory.Parent is not null &&
            StartsWithPathSegment(cleaned, currentDirectory.Name))
        {
            var workspaceCandidate = PathValueResolver.Resolve(currentDirectory.Parent.FullName, cleaned);
            if (File.Exists(workspaceCandidate))
                return workspaceCandidate;
        }

        return PathValueResolver.Resolve(currentDirectory.FullName, cleaned);
    }

    private static string ResolveSecret(string? secret, string? secretFilePath)
    {
        if (!string.IsNullOrWhiteSpace(secretFilePath))
            return File.ReadAllText(secretFilePath).Trim();

        if (string.IsNullOrWhiteSpace(secret))
            return string.Empty;

        var resolvedSecret = secret;
        return resolvedSecret!.Trim();
    }

    private static string NormalizePath(string value)
        => PathValueResolver.NormalizeSeparators(value);

    private static bool StartsWithPathSegment(string path, string segment)
    {
        var cleaned = StripCurrentDirectoryPrefix(PathValueResolver.Clean(path));
        var normalizedSegment = segment.Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(cleaned) || string.IsNullOrWhiteSpace(normalizedSegment))
            return false;

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        var firstSegment = cleaned
            .Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        return ModuleBuildPathPolicy.SamePathSegment(firstSegment, normalizedSegment);
    }

    private static string StripCurrentDirectoryPrefix(string path)
    {
        var cleaned = path;
        while (cleaned.Length >= 2 &&
               cleaned[0] == '.' &&
               (cleaned[1] == Path.DirectorySeparatorChar || cleaned[1] == Path.AltDirectorySeparatorChar))
        {
            cleaned = cleaned.Substring(2);
        }

        return cleaned;
    }

    private static ArtefactCopyMapping[]? NormalizeMappings(ArtefactCopyMapping[]? input)
    {
        if (input is null || input.Length == 0)
            return null;

        var mappings = new List<ArtefactCopyMapping>();
        foreach (var entry in input)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.Source) ||
                string.IsNullOrWhiteSpace(entry.Destination))
            {
                continue;
            }

            mappings.Add(new ArtefactCopyMapping
            {
                Source = NormalizePath(entry.Source),
                Destination = NormalizePath(entry.Destination)
            });
        }

        return mappings.Count == 0 ? null : mappings.ToArray();
    }
}
