namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleArchivePlan_AcceptsMissingXcodeGenProject()
    {
        var root = CreateSandbox();
        try
        {
            var projectDirectory = Path.Combine(root, "Generated");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "project.yml"), "name: Generated");
            var spec = new PowerForgeReleaseSpec
            {
                AppleApps = new PowerForgeAppleReleaseOptions
                {
                    ProjectRoot = root,
                    Automation = new PowerForgeAppleReleaseAutomationOptions(),
                    Apps = new[]
                    {
                        new AppleAppConfiguration
                        {
                            Name = "Generated iOS",
                            ProjectPath = "Generated/Generated.xcodeproj",
                            Scheme = "Generated",
                            GenerateProjectIfMissing = true
                        }
                    }
                }
            };

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.Archive
                });

            Assert.True(result.Success);
            Assert.True(Assert.Single(result.AppleAppPlan!.Apps).GenerateProjectIfMissing);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_ApplePlan_RejectsDuplicateStableTargetNames()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var first = Assert.Single(spec.AppleApps!.Apps);
            first.Name = "CasaRay";
            spec.AppleApps.Apps = new[]
            {
                first,
                new AppleAppConfiguration
                {
                    Name = "casaray",
                    BundleId = "com.evotecit.casaray",
                    Platform = ApplePlatform.macOS,
                    ProjectPath = "CasaRay.xcodeproj",
                    Scheme = "CasaRay",
                    AppStoreConnectAppId = "6778025328"
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Archive,
                        PlanOnly = true
                    }));

            Assert.Contains("target names must be unique", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.Upload)]
    public void Execute_AppleRemoteAction_GeneratesMissingProjectBeforeResolvingIdentity(
        PowerForgeAppleReleaseAction action)
    {
        var root = CreateSandbox();
        try
        {
            var generatedRoot = Path.Combine(root, "Generated");
            Directory.CreateDirectory(generatedRoot);
            File.WriteAllText(Path.Combine(generatedRoot, "project.yml"), "name: Generated");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.Name = "Generated iOS";
            app.ProjectPath = "Generated/Generated.xcodeproj";
            app.Scheme = "Generated";
            app.GenerateProjectIfMissing = true;
            var generationCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                getAvailableBytes: _ => throw new InvalidOperationException("Exact remote resume must skip archive preflight."),
                generateAppleProject: plan =>
                {
                    generationCalls++;
                    CreateXcodeProject(
                        Path.GetDirectoryName(plan.ProjectPath)!,
                        Path.GetFileName(plan.ProjectPath),
                        "1.2.0",
                        "9");
                    return true;
                });

            var result = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = action
                });

            Assert.True(result.Success);
            Assert.Equal(1, generationCalls);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.True(target.ProjectGenerated);
            Assert.Equal("1.2.0", target.Version);
            Assert.Equal("9", target.Build);
            if (action == PowerForgeAppleReleaseAction.Upload)
                Assert.True(target.ResumedExistingBuild);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatus_GeneratesBeforeResolvingIncrementExistingBuild()
    {
        var root = CreateSandbox();
        try
        {
            var generatedRoot = Path.Combine(root, "Generated");
            Directory.CreateDirectory(generatedRoot);
            File.WriteAllText(Path.Combine(generatedRoot, "project.yml"), "name: Generated");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.ProjectPath = "Generated/Generated.xcodeproj";
            app.Scheme = "Generated";
            app.GenerateProjectIfMissing = true;
            app.MarketingVersion = "1.3.0";
            app.BuildNumberPolicy = AppleBuildNumberPolicy.IncrementExisting;
            AppStoreConnectReleaseStateRequest? observed = null;

            var result = CreateAppleAutomationService(
                    request =>
                    {
                        observed = request;
                        return CreateReleaseState(request, "VALID");
                    },
                    generateAppleProject: plan =>
                    {
                        CreateXcodeProject(
                            Path.GetDirectoryName(plan.ProjectPath)!,
                            Path.GetFileName(plan.ProjectPath),
                            "1.2.0",
                            "9");
                        return true;
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });

            Assert.True(result.Success);
            Assert.NotNull(observed);
            Assert.Equal("1.3.0", observed!.VersionString);
            Assert.Equal("10", observed.BuildNumber);
            var local = new XcodeProjectVersionEditor().Read(Path.Combine(generatedRoot, "Generated.xcodeproj"));
            Assert.Equal("1.2.0", local.MarketingVersion);
            Assert.Equal("9", local.BuildNumber);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatus_RegeneratesBeforeReadingReleaseIdentity()
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.0.0", "1");
            File.WriteAllText(Path.Combine(root, "project.yml"), "name: CasaRay");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            Assert.Single(spec.AppleApps!.Apps).RegenerateProject = true;
            AppStoreConnectReleaseStateRequest? observed = null;

            var result = CreateAppleAutomationService(
                    request =>
                    {
                        observed = request;
                        return CreateReleaseState(request, "VALID");
                    },
                    generateAppleProject: plan =>
                    {
                        CreateXcodeProject(root, "CasaRay.xcodeproj", "2.0.0", "9");
                        return true;
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = PowerForgeAppleReleaseAction.Status
                    });

            Assert.True(result.Success);
            Assert.NotNull(observed);
            Assert.Equal("2.0.0", observed!.VersionString);
            Assert.Equal("9", observed.BuildNumber);
            Assert.True(Assert.Single(result.AppleReceipt!.Targets).ProjectGenerated);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(PowerForgeAppleReleaseAction.Status)]
    [InlineData(PowerForgeAppleReleaseAction.Prepare)]
    public void Execute_NonArchiveAppleAction_UsesConfiguredIdentityWithoutMutatingSource(
        PowerForgeAppleReleaseAction action)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = Assert.Single(spec.AppleApps!.Apps);
            app.MarketingVersion = "1.3.0";
            app.BuildNumber = "10";
            var observed = new List<(string? Version, string? Build)>();

            var result = CreateAppleAutomationService(
                    request =>
                    {
                        observed.Add((request.VersionString, request.BuildNumber));
                        return CreateReleaseState(request, "VALID");
                    },
                    prepareAppleDistribution: request =>
                    {
                        observed.Add((request.VersionString, request.BuildNumber));
                        return CreateSuccessfulPreparation(request);
                    })
                .Execute(
                    spec,
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = Path.Combine(root, "powerforge.release.json"),
                        AppleAction = action
                    });

            Assert.True(result.Success);
            Assert.All(observed, value =>
            {
                Assert.Equal("1.3.0", value.Version);
                Assert.Equal("10", value.Build);
            });
            var local = new XcodeProjectVersionEditor().Read(Path.Combine(root, "CasaRay.xcodeproj"));
            Assert.Equal("1.2.0", local.MarketingVersion);
            Assert.Equal("9", local.BuildNumber);
        }
        finally
        {
            TryDelete(root);
        }
    }

}
