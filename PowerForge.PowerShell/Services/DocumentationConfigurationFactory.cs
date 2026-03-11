namespace PowerForge;

internal sealed class DocumentationConfigurationFactory
{
    public IReadOnlyList<IConfigurationSegment> Create(DocumentationConfigurationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var segments = new List<IConfigurationSegment>
        {
            new ConfigurationDocumentationSegment
            {
                Configuration = new DocumentationConfiguration
                {
                    Path = request.Path,
                    PathReadme = request.PathReadme
                }
            }
        };

        var emitBuildSegment =
            request.Enable ||
            request.StartClean ||
            request.UpdateWhenNew ||
            request.SyncExternalHelpToProjectRoot ||
            request.SkipExternalHelp ||
            request.SkipAboutTopics ||
            request.SkipFallbackExamples ||
            request.ExternalHelpCultureSpecified ||
            request.ExternalHelpFileNameSpecified ||
            request.AboutTopicsSourcePathSpecified;

        if (emitBuildSegment)
        {
            segments.Add(new ConfigurationBuildDocumentationSegment
            {
                Configuration = new BuildDocumentationConfiguration
                {
                    Enable = request.Enable,
                    StartClean = request.StartClean,
                    UpdateWhenNew = request.UpdateWhenNew,
                    SyncExternalHelpToProjectRoot = request.SyncExternalHelpToProjectRoot,
                    Tool = DocumentationTool.PowerForge,
                    IncludeAboutTopics = !request.SkipAboutTopics,
                    GenerateFallbackExamples = !request.SkipFallbackExamples,
                    GenerateExternalHelp = !request.SkipExternalHelp,
                    ExternalHelpCulture = request.ExternalHelpCulture,
                    ExternalHelpFileName = request.ExternalHelpFileName,
                    AboutTopicsSourcePath = request.AboutTopicsSourcePath ?? Array.Empty<string>()
                }
            });
        }

        return segments;
    }
}
