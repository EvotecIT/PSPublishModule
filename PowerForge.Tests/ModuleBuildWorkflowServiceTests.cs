using PowerForge;

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

    private static ModulePipelinePlan CreatePlan()
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
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: Array.Empty<string>(),
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: Array.Empty<ConfigurationPublishSegment>(),
            artefacts: Array.Empty<ConfigurationArtefactSegment>(),
            installEnabled: false,
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
}
