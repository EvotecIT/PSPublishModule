namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_AppleStatus_ObservesLatestIntegrityRejectedDeliveryWithoutMutationOrQualification()
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var observedIds = new List<string>();
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                prepareAppleDistribution: _ => throw new InvalidOperationException("Status must not prepare distribution."),
                distributeTestFlight: _ => throw new InvalidOperationException("Status must not distribute."),
                getAppleBuildUpload: (_, id) =>
                {
                    observedIds.Add(id);
                    return new AppStoreConnectBuildUploadInfo
                    {
                        Id = id,
                        MarketingVersion = "1.2.0",
                        BuildNumber = "9",
                        Platform = "IOS",
                        State = "FAILED",
                        Errors = [new() { Code = "ITMS-TEST", Description = "The uploaded bundle was rejected." }]
                    };
                });
            var plan = CreateAppleStatusDeliveryPlan(service, spec, root, source);
            WriteAppleStatusDelivery(plan, "older-delivery");
            var delivery = WriteAppleStatusDelivery(plan, "latest-delivery");
            new AppleReleaseReceiptStore().WriteAttempt(plan, new PowerForgeAppleReleaseReceipt
            {
                Action = PowerForgeAppleReleaseAction.Upload,
                SourceCommit = source,
                OperationPhase = "UploadIntegrityFailed",
                Success = false,
                ErrorMessage = "The private Apple upload snapshot changed.",
                Targets = delivery.Targets
            });

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(new[] { "latest-delivery" }, observedIds);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("latest-delivery", target.BuildUploadId);
            Assert.Equal("FAILED", target.BuildUploadState);
            var diagnostic = Assert.Single(target.Diagnostics, value => value.Code == "APPLE_BUILD_UPLOAD_REJECTED");
            Assert.Equal("error", diagnostic.Severity);
            Assert.Contains("[ITMS-TEST] The uploaded bundle was rejected.", diagnostic.Evidence);
            Assert.False(diagnostic.Retryable);
            Assert.Null(target.BuildId);
            Assert.Null(target.BuildProcessingState);
            Assert.False(target.ArchiveCreated);
            Assert.False(target.UploadPerformed);
            Assert.False(target.ResumedExistingBuild);
            Assert.False(target.AdoptedExistingBuild);
            Assert.Null(target.UploadAttestationAttemptId);
            Assert.Null(target.UploadExecutionSha256);
            Assert.DoesNotContain(new AppleReleaseReceiptStore().ReadAll(plan),
                receipt => receipt.OperationPhase == "UploadAttested");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_AppleStatus_ObservesCurrentDeliveryWhenAnOlderAppStoreBuildIsSelected()
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var reads = 0;
            var service = CreateAppleAutomationService(request =>
            {
                var state = CreateReleaseState(request, null);
                state.Platforms[0].SelectedBuild = new()
                {
                    Id = "older-selected-build", Version = "8", MarketingVersion = "1.1", ProcessingState = "VALID"
                };
                return state;
            }, getAppleBuildUpload: (_, id) =>
            {
                reads++;
                return new() { Id = id, MarketingVersion = "1.2.0", BuildNumber = "9", Platform = "IOS", State = "FAILED" };
            });
            var plan = CreateAppleStatusDeliveryPlan(service, spec, root, source);
            WriteAppleStatusDelivery(plan, "current-upload");
            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"), AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, reads);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("9", target.Build);
            Assert.Equal("current-upload", target.BuildUploadId);
            Assert.Contains(target.Diagnostics, value => value.Code == "APPLE_BUILD_UPLOAD_REJECTED");
        }
        finally { TryDelete(root); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("app")]
    [InlineData("bundle")]
    [InlineData("target")]
    [InlineData("platform")]
    [InlineData("version")]
    [InlineData("build")]
    public void Execute_AppleStatus_ExcludesUnrelatedDeliveryCheckpoints(string mismatch)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var uploadReads = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                getAppleBuildUpload: (_, _) => { uploadReads++; return new() { State = "FAILED" }; });
            var plan = CreateAppleStatusDeliveryPlan(service, spec, root, source);
            WriteAppleStatusDelivery(plan, "unrelated-delivery", receipt =>
            {
                var target = Assert.Single(receipt.Targets);
                switch (mismatch)
                {
                    case "source": receipt.SourceCommit = new string('a', 40); break;
                    case "app": target.AppId = "other-app"; break;
                    case "bundle": target.BundleId = "com.example.other"; break;
                    case "target": target.Name = "other-target"; break;
                    case "platform": target.Platform = ApplePlatform.macOS; break;
                    case "version": target.Version = "2.0.0"; break;
                    case "build": target.Build = "10"; break;
                }
            });

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(0, uploadReads);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Null(target.BuildUploadId);
            Assert.Null(target.BuildUploadState);
            Assert.Null(target.BuildId);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("build")]
    [InlineData("platform")]
    [InlineData("missing")]
    [InlineData("unavailable")]
    public void Execute_AppleStatus_DoesNotUseUnknownOrMismatchedRemoteDeliveryState(string response)
    {
        var root = CreateSandbox();
        try
        {
            var (spec, source) = CreateAppleStatusDeliveryFixture(root);
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, processingState: null),
                getAppleBuildUpload: (_, id) =>
                {
                    if (response == "missing") return null;
                    if (response == "unavailable") throw new InvalidOperationException("Delivery endpoint unavailable.");
                    return new()
                    {
                        Id = response == "id" ? "other-delivery" : id,
                        MarketingVersion = response == "version" ? "2.0.0" : "1.2.0",
                        BuildNumber = response == "build" ? "10" : "9",
                        Platform = response == "platform" ? "MAC_OS" : "IOS",
                        State = "COMPLETE"
                    };
                });
            var plan = CreateAppleStatusDeliveryPlan(service, spec, root, source);
            WriteAppleStatusDelivery(plan, "recorded-delivery");

            var result = service.Execute(spec, new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.Status,
                AppleSourceCommit = source
            });

            Assert.True(result.Success, result.ErrorMessage);
            var target = Assert.Single(result.AppleReceipt!.Targets);
            Assert.Equal("recorded-delivery", target.BuildUploadId);
            Assert.Null(target.BuildUploadState);
            Assert.Null(target.BuildId);
            Assert.Null(target.BuildProcessingState);
            Assert.False(target.UploadPerformed);
            Assert.Contains(target.Diagnostics, value => value.Code ==
                (response is "missing" or "unavailable"
                    ? "APPLE_BUILD_UPLOAD_OBSERVATION_UNAVAILABLE"
                    : "APPLE_BUILD_UPLOAD_IDENTITY_MISMATCH"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static (PowerForgeReleaseSpec Spec, string Source) CreateAppleStatusDeliveryFixture(string root)
    {
        CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
        var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
        File.WriteAllText(keyPath, "synthetic-key-not-used");
        return (CreateAppleAutomationSpec(root, keyPath), EnsureTestSourceCommit(root));
    }

    private static PowerForgeAppleReleasePlan CreateAppleStatusDeliveryPlan(
        PowerForgeReleaseService service,
        PowerForgeReleaseSpec spec,
        string root,
        string source)
    {
        var planned = service.Execute(spec, new PowerForgeReleaseRequest
        {
            ConfigPath = Path.Combine(root, "powerforge.release.json"),
            AppleAction = PowerForgeAppleReleaseAction.Status,
            AppleSourceCommit = source,
            PlanOnly = true
        });
        Assert.True(planned.Success, planned.ErrorMessage);
        return planned.AppleAppPlan!;
    }

    private static PowerForgeAppleReleaseReceipt WriteAppleStatusDelivery(
        PowerForgeAppleReleasePlan plan,
        string uploadId,
        Action<PowerForgeAppleReleaseReceipt>? change = null)
    {
        var app = Assert.Single(plan.Apps);
        var attempt = Guid.NewGuid().ToString("N");
        var receipt = new PowerForgeAppleReleaseReceipt
        {
            AttemptId = attempt,
            Action = PowerForgeAppleReleaseAction.Upload,
            SourceCommit = plan.SourceCommit,
            OperationPhase = "UploadCompleted",
            Success = false,
            Targets = [new()
            {
                Name = app.Name,
                BundleId = app.BundleId,
                AppId = app.AppStoreConnectAppId,
                Platform = app.Platform,
                Configuration = app.Configuration,
                ProjectPath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ProjectPath).Replace('\\', '/'),
                IsWorkspace = app.IsWorkspace,
                Scheme = app.Scheme,
                ArchiveVariant = app.ArchiveVariant,
                Destination = app.Destination,
                DistributionRoute = app.DistributionRoute,
                Version = "1.2.0",
                Build = "9",
                BuildUploadId = uploadId,
                UploadPerformed = true,
                UploadAttestationAttemptId = attempt,
                ArchivePath = FrameworkCompatibility.GetRelativePath(plan.ProjectRoot, app.ArchivePath).Replace('\\', '/'),
                ArchiveSha256 = new string('A', 64),
                UploadExecutionSha256 = new string('B', 64)
            }]
        };
        change?.Invoke(receipt);
        new AppleReleaseReceiptStore().WriteAttempt(plan, receipt);
        return receipt;
    }
}
