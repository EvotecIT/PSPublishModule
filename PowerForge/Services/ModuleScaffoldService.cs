using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Creates a basic PowerShell module folder structure and copies template files.
/// </summary>
public sealed class ModuleScaffoldService
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new scaffolder that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ModuleScaffoldService(ILogger logger) => _logger = logger;

    /// <summary>
    /// Ensures the module scaffold exists under <see cref="ModuleScaffoldSpec.ProjectRoot"/>.
    /// If the folder already exists, no changes are made.
    /// </summary>
    public ModuleScaffoldResult EnsureScaffold(ModuleScaffoldSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ProjectRoot))
            throw new ArgumentException("ProjectRoot is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ModuleName))
            throw new ArgumentException("ModuleName is required.", nameof(spec));

        var projectRoot = Path.GetFullPath(spec.ProjectRoot.Trim().Trim('"'));
        var moduleName = spec.ModuleName.Trim();

        if (Directory.Exists(projectRoot))
        {
            _logger.Info($"Module {moduleName} ({projectRoot}) already exists. Skipping initial steps.");
            return new ModuleScaffoldResult(projectRoot, created: false, moduleGuid: null);
        }

        IReadOnlyDictionary<string, string>? embeddedTemplates = null;
        var useEmbeddedTemplates = string.IsNullOrWhiteSpace(spec.TemplateRootPath) &&
                                   ModuleScaffoldTemplateStore.TryLoadDefaults(out embeddedTemplates);
        var templateRoot = useEmbeddedTemplates ? null : ResolveTemplateRoot(spec.TemplateRootPath);
        var basePath = Directory.GetParent(projectRoot)?.FullName;
        if (!string.IsNullOrWhiteSpace(basePath) && !Directory.Exists(basePath))
            throw new DirectoryNotFoundException($"Base path does not exist: {basePath}");

        _logger.Info($"Preparing module structure for {moduleName} in {basePath}");

        Directory.CreateDirectory(projectRoot);
        foreach (var folder in new[] { "Private", "Public", "Examples", "Ignore", "Build", Path.Combine("Help", "About") })
            Directory.CreateDirectory(Path.Combine(projectRoot, folder));

        var guid = Guid.NewGuid().ToString();
        var filesToCopy = new (string Source, string Dest, bool Patch)[]
        {
            (useEmbeddedTemplates ? "Example-Gitignore.txt" : Path.Combine(templateRoot!, "Example-Gitignore.txt"), Path.Combine(projectRoot, ".gitignore"), false),
            (useEmbeddedTemplates ? "Example-CHANGELOG.MD" : Path.Combine(templateRoot!, "Example-CHANGELOG.MD"), Path.Combine(projectRoot, "CHANGELOG.MD"), false),
            (useEmbeddedTemplates ? "Example-README.MD" : Path.Combine(templateRoot!, "Example-README.MD"), Path.Combine(projectRoot, "README.MD"), false),
            (useEmbeddedTemplates ? "Example-LicenseMIT.txt" : Path.Combine(templateRoot!, "Example-LicenseMIT.txt"), Path.Combine(projectRoot, "LICENSE"), false),
            (useEmbeddedTemplates ? "Example-ModuleBuilder.txt" : Path.Combine(templateRoot!, "Example-ModuleBuilder.txt"), Path.Combine(projectRoot, "Build", "Build-Module.ps1"), true),
            (useEmbeddedTemplates ? "Example-ModulePSM1.txt" : Path.Combine(templateRoot!, "Example-ModulePSM1.txt"), Path.Combine(projectRoot, $"{moduleName}.psm1"), false),
            (useEmbeddedTemplates ? "Example-ModulePSD1.txt" : Path.Combine(templateRoot!, "Example-ModulePSD1.txt"), Path.Combine(projectRoot, $"{moduleName}.psd1"), true),
        };

        foreach (var f in filesToCopy)
        {
            if (File.Exists(f.Dest)) continue;

            _logger.Info($"   📄 Copying '{Path.GetFileName(f.Dest)}' ({f.Source})");
            if (useEmbeddedTemplates)
            {
                File.WriteAllText(f.Dest, embeddedTemplates![f.Source], new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            else
            {
                File.Copy(f.Source, f.Dest, overwrite: false);
            }

            if (f.Patch)
                PatchInitialModuleTemplate(f.Dest, moduleName, guid);
        }

        EnsureAboutTopicSeed(projectRoot, moduleName);

        _logger.Success($"Preparing module structure for {moduleName} in {basePath}. Completed.");
        return new ModuleScaffoldResult(projectRoot, created: true, moduleGuid: guid);
    }

    private static void PatchInitialModuleTemplate(string filePath, string moduleName, string guid)
    {
        var content = File.ReadAllText(filePath);
        content = ReplaceTemplateToken(content, "GUID", guid);
        content = ReplaceTemplateToken(content, "ModuleName", moduleName);
        File.WriteAllText(filePath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string ReplaceTemplateToken(string content, string tokenName, string value)
    {
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(tokenName))
            return content;

        var pattern = $@"`?\${Regex.Escape(tokenName)}(?![A-Za-z0-9_])";
        return Regex.Replace(content, pattern, value, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private void EnsureAboutTopicSeed(string projectRoot, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(moduleName))
            return;

        var service = new AboutTopicTemplateService();
        try
        {
            service.Generate(new AboutTopicTemplateRequest
            {
                TopicName = $"about_{moduleName}_Overview",
                OutputPath = Path.Combine("Help", "About"),
                Force = false,
                ShortDescription = $"Overview for {moduleName} module.",
                WorkingDirectory = projectRoot
            });
        }
        catch (IOException)
        {
            // Existing seed file is fine.
        }
    }

    private static string ResolveTemplateRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            var full = Path.GetFullPath(explicitRoot!.Trim().Trim('"'));
            if (!Directory.Exists(full))
                throw new DirectoryNotFoundException($"Template directory not found: {full}");
            if (!IsTemplateRoot(full))
                throw new DirectoryNotFoundException($"Template directory does not contain Example-* template files: {full}");
            return full;
        }

        // Try to locate templates relative to the app base directory / current directory.
        // PowerShell host base dir can be unreliable, so callers (cmdlets) should prefer passing TemplateRootPath when possible.
        var startPoints = new List<string>();
        if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
            startPoints.Add(AppContext.BaseDirectory);
        try
        {
            var cwd = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(cwd)) startPoints.Add(cwd);
        }
        catch { /* ignore */ }

        foreach (var start in startPoints.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var current = start;
            for (var i = 0; i < 10; i++)
            {
                if (string.IsNullOrWhiteSpace(current)) break;

                var direct = Path.Combine(current, "Data");
                if (IsTemplateRoot(direct)) return direct;

                var moduleData = Path.Combine(current, "Module", "Data");
                if (IsTemplateRoot(moduleData)) return moduleData;

                current = Directory.GetParent(current)?.FullName;
            }
        }

        throw new DirectoryNotFoundException("Module scaffold templates not found (expected embedded defaults or template files under 'Data' or 'Module\\Data').");
    }

    private static bool IsTemplateRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
        return File.Exists(Path.Combine(path, "Example-ModuleBuilder.txt")) &&
               File.Exists(Path.Combine(path, "Example-ModulePSD1.txt")) &&
               File.Exists(Path.Combine(path, "Example-ModulePSM1.txt"));
    }
}
