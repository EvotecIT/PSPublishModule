using PowerForge;
using PowerForge.ConsoleShared;
using Spectre.Console;

namespace PowerForge.Tests;

public sealed class ModuleBuildWorkflowServiceTests
{
    [Fact]
    public void Execute_runs_noninteractive_pipeline_and_writes_summary()
    {
        var plan = CreatePlan();
        var result = CreateResult(plan);
        var summaries = new List<ModulePipelineResult>();

        var service = new ModuleBuildWorkflowService(
            new NullLogger(),
            planPipeline: spec =>
            {
                Assert.Equal("SampleModule", spec.Build.Name);
                return plan;
            },
            runPipeline: (spec, planned) =>
            {
                Assert.Same(plan, planned);
                return result;
            },
            writeSummary: summaries.Add);

        var workflow = service.Execute(CreatePreparedContext(), interactive: false, configLabel: "cmdlet");

        Assert.True(workflow.Succeeded);
        Assert.False(workflow.UsedInteractiveView);
        Assert.Same(plan, workflow.Plan);
        Assert.Same(result, workflow.Result);
        Assert.Single(summaries);
    }

    [Fact]
    public void Execute_uses_interactive_delegate_when_requested()
    {
        var plan = CreatePlan();
        var result = CreateResult(plan);
        var interactiveCalls = 0;

        var service = new ModuleBuildWorkflowService(
            new NullLogger(),
            planPipeline: _ => plan,
            runInteractive: (spec, planned, label) =>
            {
                interactiveCalls++;
                Assert.Same(plan, planned);
                Assert.Equal("dsl", label);
                return result;
            });

        var workflow = service.Execute(CreatePreparedContext(), interactive: true, configLabel: "dsl");

        Assert.True(workflow.Succeeded);
        Assert.True(workflow.UsedInteractiveView);
        Assert.Equal(1, interactiveCalls);
    }

    [Fact]
    public void Execute_captures_policy_failure_and_marks_summary_write()
    {
        var plan = CreatePlan();
        var result = CreateResult(plan);
        var policy = new BuildDiagnosticsPolicyEvaluation
        {
            PolicyViolated = true,
            FailureReason = "new diagnostics"
        };
        var summaries = new List<ModulePipelineResult>();

        var service = new ModuleBuildWorkflowService(
            new NullLogger(),
            planPipeline: _ => plan,
            runPipeline: (_, _) => throw new ModulePipelineDiagnosticsPolicyException(result, policy, "policy failed"),
            writeSummary: summaries.Add);

        var workflow = service.Execute(CreatePreparedContext(), interactive: false, configLabel: "cmdlet");

        Assert.False(workflow.Succeeded);
        Assert.NotNull(workflow.PolicyFailure);
        Assert.True(workflow.WrotePolicySummary);
        Assert.Single(summaries);
        Assert.Same(result, summaries[0]);
    }

    [Fact]
    public void ModuleStepPresentation_ProvidesCanonicalTitlesAndSemanticTargets()
    {
        var artefact = new ConfigurationArtefactSegment
        {
            ArtefactType = ArtefactType.Unpacked,
            Configuration = new ArtefactConfiguration { ID = "ToGitHub" }
        };
        var publish = new ConfigurationPublishSegment
        {
            Configuration = new PublishConfiguration
            {
                Destination = PublishDestination.GitHub,
                RepositoryName = "SampleModule"
            }
        };
        var plan = CreatePlan(
            artefacts: [artefact],
            publishes: [publish],
            installEnabled: true);
        var steps = ModulePipelineStep.Create(plan);

        var artefactDisplay = ModulePipelineStepPresentation.Create(
            Assert.Single(steps, step => step.Kind == ModulePipelineStepKind.Artefact),
            plan);
        var publishDisplay = ModulePipelineStepPresentation.Create(
            Assert.Single(steps, step => step.Kind == ModulePipelineStepKind.Publish),
            plan);
        var installDisplay = ModulePipelineStepPresentation.Create(
            Assert.Single(steps, step => step.Kind == ModulePipelineStepKind.Install),
            plan);

        Assert.Equal("Pack artefact", artefactDisplay.Title);
        Assert.Equal("Unpacked (ToGitHub)", artefactDisplay.Target);
        Assert.Equal("Publish", publishDisplay.Title);
        Assert.Equal("GitHub (SampleModule)", publishDisplay.Target);
        Assert.Equal("Install", installDisplay.Title);
        Assert.Equal("AutoRevision, keep 3", installDisplay.Target);

        var transportedItems = ModulePipelineProgressItemFactory.Create(plan);
        var transportedArtefact = Assert.Single(
            transportedItems,
            item => item.Kind == ModulePipelineStepKind.Artefact.ToString());
        Assert.Equal(artefactDisplay.Title, transportedArtefact.Title);
        Assert.Equal(artefactDisplay.Target, transportedArtefact.Target);
    }

    [Fact]
    public void DirectModuleConsole_UsesSharedRichRowsInCanonicalOrder()
    {
        using var writer = new StringWriter();
        var console = CreateConsole(writer);
        var plan = CreatePlan(
            artefacts:
            [
                new ConfigurationArtefactSegment
                {
                    ArtefactType = ArtefactType.Unpacked,
                    Configuration = new ArtefactConfiguration { ID = "ToGitHub" }
                }
            ],
            publishes:
            [
                new ConfigurationPublishSegment
                {
                    Configuration = new PublishConfiguration
                    {
                        Destination = PublishDestination.GitHub,
                        RepositoryName = "SampleModule"
                    }
                }
            ],
            installEnabled: true);
        var steps = ModulePipelineStep.Create(plan);

        var result = SpectreModulePipelineConsoleUi.RunInteractive(
            console,
            plan,
            "powerforge.json",
            progress =>
            {
                foreach (var step in steps)
                {
                    progress.StepStarting(step);
                    progress.StepCompleted(step);
                }

                return CreateResult(plan);
            });

        Assert.Same(plan, result.Plan);
        var output = writer.ToString();
        Assert.Contains("PowerForge", output, StringComparison.Ordinal);
        Assert.Contains("SampleModule 1.0.0", output, StringComparison.Ordinal);
        Assert.Contains("Unpacked (ToGitHub)", output, StringComparison.Ordinal);
        Assert.Contains("GitHub (SampleModule)", output, StringComparison.Ordinal);
        Assert.Contains("AutoRevision, keep 3", output, StringComparison.Ordinal);

        var stage = output.LastIndexOf("Stage to staging", StringComparison.Ordinal);
        var artefact = output.LastIndexOf("Pack artefact", StringComparison.Ordinal);
        var publish = output.LastIndexOf("Publish", StringComparison.Ordinal);
        var install = output.LastIndexOf("Install", StringComparison.Ordinal);
        Assert.True(stage >= 0, output);
        Assert.True(artefact > stage, output);
        Assert.True(publish > artefact, output);
        Assert.True(install > publish, output);
    }

    private static ModuleBuildPreparedContext CreatePreparedContext()
    {
        return new ModuleBuildPreparedContext
        {
            ModuleName = "SampleModule",
            ProjectRoot = @"C:\repo\SampleModule",
            UseLegacy = false,
            PipelineSpec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = @"C:\repo\SampleModule",
                    Version = "1.0.0"
                }
            }
        };
    }

    private static ModulePipelinePlan CreatePlan(
        ConfigurationArtefactSegment[]? artefacts = null,
        ConfigurationPublishSegment[]? publishes = null,
        bool installEnabled = false)
    {
        return new ModulePipelinePlan(
            moduleName: "SampleModule",
            projectRoot: @"C:\repo\SampleModule",
            expectedVersion: "1.0.0",
            resolvedVersion: "1.0.0",
            preRelease: null,
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = "SampleModule",
                SourcePath = @"C:\repo\SampleModule",
                Version = "1.0.0"
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: Array.Empty<string>(),
            requiredModules: Array.Empty<RequiredModuleReference>(),
            externalModuleDependencies: Array.Empty<string>(),
            requiredModulesForPackaging: Array.Empty<RequiredModuleReference>(),
            information: null,
            documentation: null,
            delivery: null,
            documentationBuild: null,
            compatibilitySettings: null,
            fileConsistencySettings: null,
            validationSettings: null,
            formatting: null,
            importModules: null,
            placeHolders: Array.Empty<PlaceHolderReplacement>(),
            placeHolderOption: null,
            commandModuleDependencies: new Dictionary<string, string[]>(),
            testsAfterMerge: Array.Empty<TestConfiguration>(),
            actions: Array.Empty<ConfigurationActionSegment>(),
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: publishes ?? Array.Empty<ConfigurationPublishSegment>(),
            artefacts: artefacts ?? Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: installEnabled,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 3,
            installRoots: Array.Empty<string>(),
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: Array.Empty<string>(),
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: true,
            deleteGeneratedStagingAfterRun: true);
    }

    private static ModulePipelineResult CreateResult(ModulePipelinePlan plan)
    {
        return new ModulePipelineResult(
            plan: plan,
            buildResult: new ModuleBuildResult(
                stagingPath: @"C:\temp\staging",
                manifestPath: @"C:\temp\staging\SampleModule.psd1",
                exports: new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())),
            installResult: null,
            documentationResult: null,
            fileConsistencyReport: null,
            fileConsistencyStatus: null,
            fileConsistencyEncodingFix: null,
            fileConsistencyLineEndingFix: null,
            compatibilityReport: null,
            validationReport: null,
            diagnostics: Array.Empty<BuildDiagnostic>(),
            diagnosticsBaseline: null,
            diagnosticsPolicy: null,
            publishResults: Array.Empty<ModulePublishResult>(),
            artefactResults: Array.Empty<ArtefactBuildResult>());
    }

    private static IAnsiConsole CreateConsole(TextWriter writer)
        => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new TestConsoleOutput(writer),
            Ansi = AnsiSupport.Yes,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.Yes
        });

    private sealed class TestConsoleOutput : IAnsiConsoleOutput
    {
        internal TestConsoleOutput(TextWriter writer)
            => Writer = writer;

        public TextWriter Writer { get; }
        public bool IsTerminal => true;
        public int Width => 140;
        public int Height => 24;
        public void SetEncoding(System.Text.Encoding encoding) { }
    }
}
