using System.IO;

namespace PowerForge;

internal sealed class ModuleBuildScaffoldBootstrapService
{
    private readonly ILogger _logger;
    private readonly Func<ILogger, ModuleScaffoldService> _createScaffolder;

    public ModuleBuildScaffoldBootstrapService(
        ILogger logger,
        Func<ILogger, ModuleScaffoldService>? createScaffolder = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _createScaffolder = createScaffolder ?? (currentLogger => new ModuleScaffoldService(currentLogger));
    }

    public ModuleBuildScaffoldBootstrapResult EnsureScaffold(ModuleBuildPreparedContext context, string? moduleBase)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(context.BasePathForScaffold))
            return new ModuleBuildScaffoldBootstrapResult { Succeeded = true, Attempted = false };

        var basePath = context.BasePathForScaffold!;
        if (!Directory.Exists(basePath))
        {
            _logger.Error($"Path '{basePath}' does not exist. Please create it before continuing.");
            return new ModuleBuildScaffoldBootstrapResult { Succeeded = false, Attempted = false };
        }

        string? templateRoot = null;
        if (!ModuleScaffoldTemplateStore.TryLoadDefaults(out _)
            && !string.IsNullOrWhiteSpace(moduleBase))
        {
            var candidate = Path.Combine(moduleBase, "Data");
            if (Directory.Exists(candidate))
                templateRoot = candidate;
        }

        _createScaffolder(_logger).EnsureScaffold(new ModuleScaffoldSpec
        {
            ProjectRoot = context.ProjectRoot,
            ModuleName = context.ModuleName,
            TemplateRootPath = templateRoot
        });

        return new ModuleBuildScaffoldBootstrapResult { Succeeded = true, Attempted = true };
    }
}
