namespace PowerForge.Tests;

public sealed class AppleReleaseReceiptStoreTests
{
    [Fact]
    public void WriteAttempt_PreservesEveryAttemptAndChainsTheLatestReceipt()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            var store = new AppleReleaseReceiptStore();
            var first = CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: true);
            var second = CreateReceipt(PowerForgeAppleReleaseAction.Status, success: true);

            store.WriteAttempt(plan, first);
            store.WriteAttempt(plan, second);

            var receipts = store.ReadAll(plan);
            Assert.Equal(2, receipts.Length);
            Assert.Equal(2, Directory.GetFiles(plan.ReceiptHistoryPath, "*.json").Length);
            Assert.Equal(first.ReceiptSha256, second.PreviousReceiptSha256);
            Assert.NotEqual(first.ReceiptSha256, second.ReceiptSha256);
            Assert.Equal(second.ReceiptSha256, receipts[0].ReceiptSha256);
            Assert.True(File.Exists(plan.ReceiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ReadAll_RejectsTamperedImmutableReceipt()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            var store = new AppleReleaseReceiptStore();
            store.WriteAttempt(plan, CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: false));
            var historyPath = Assert.Single(Directory.GetFiles(plan.ReceiptHistoryPath, "*.json"));
            var json = File.ReadAllText(historyPath);
            File.WriteAllText(
                historyPath,
                json.Replace("\"success\": false", "\"success\": true", StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(() => store.ReadAll(plan));
            Assert.Contains("integrity validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ReadAll_RejectsLatestReceiptThatPointsAtDifferentValidHistoryEntry()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            var store = new AppleReleaseReceiptStore();
            store.WriteAttempt(plan, CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: true));
            store.WriteAttempt(plan, CreateReceipt(PowerForgeAppleReleaseAction.Status, success: true));
            var latest = store.ReadAll(plan)[0];
            var declaredHistory = Path.Combine(plan.ProjectRoot, latest.HistoryPath!);
            var other = Assert.Single(Directory.GetFiles(plan.ReceiptHistoryPath, "*.json"), path =>
                !string.Equals(path, declaredHistory, StringComparison.OrdinalIgnoreCase));
            File.Copy(other, declaredHistory, overwrite: true);

            var exception = Assert.Throws<InvalidOperationException>(() => store.ReadAll(plan));

            Assert.Contains("does not contain its declared receipt", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void WriteAttempt_ReconstructsLatestFromValidatedHistoryWhenLatestIsMissing()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            var store = new AppleReleaseReceiptStore();
            var first = CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: true);
            store.WriteAttempt(plan, first);
            File.Delete(plan.ReceiptPath);

            var second = CreateReceipt(PowerForgeAppleReleaseAction.Status, success: true);
            store.WriteAttempt(plan, second);

            Assert.Equal(first.ReceiptSha256, second.PreviousReceiptSha256);
            Assert.Equal(2, store.ReadAll(plan).Length);
            Assert.True(File.Exists(plan.ReceiptPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void WriteAttempt_PublishesImmutableHistoryWithoutExposingTemporaryEntries()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            var checkedAt = DateTimeOffset.Parse("2026-08-09T20:00:00Z");
            var store = new AppleReleaseReceiptStore(() => checkedAt);
            var receipt = CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: true);
            receipt.AttemptId = "0123456789abcdef0123456789abcdef";

            store.WriteAttempt(plan, receipt);

            var historyPath = Assert.Single(Directory.GetFiles(plan.ReceiptHistoryPath, "*.json"));
            Assert.Equal(receipt.ReceiptSha256, Assert.Single(store.ReadAll(plan)).ReceiptSha256);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(plan.ReceiptHistoryPath)!, "*.receipt.tmp"));
            Assert.True(new FileInfo(historyPath).Length > 0);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ComputeReceiptSha256Json_IsStableForFuturePropertiesAndPropertyOrder()
    {
        const string receipt =
            "{\"targets\":[],\"receiptSha256\":\"ignored\",\"schemaVersion\":4,\"futureFlag\":false,\"action\":\"Upload\"}";

        Assert.Equal(
            "827f9ae0f51e40e91517f677f7cd3d870dc20058c47d680af76918441e7b0f5f",
            AppleReleaseReceiptStore.ComputeReceiptSha256Json(receipt));
    }

    [Fact]
    public void WriteAttempt_PreservesLegacyLatestBeforeReplacingIt()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ReceiptPath)!);
            const string legacy =
                "{\"schemaVersion\":3,\"action\":\"Upload\",\"sourceCommit\":\"0123456789abcdef0123456789abcdef01234567\",\"checkedAt\":\"2026-08-01T10:00:00+00:00\",\"success\":false,\"targets\":[]}";
            File.WriteAllText(plan.ReceiptPath, legacy);

            var store = new AppleReleaseReceiptStore();
            store.WriteAttempt(plan, CreateReceipt(PowerForgeAppleReleaseAction.Status, success: true));

            var receipts = store.ReadAll(plan);
            Assert.Equal(2, receipts.Length);
            var legacyPath = Assert.Single(Directory.GetFiles(plan.ReceiptHistoryPath, "*legacy*.json"));
            Assert.Equal(legacy, File.ReadAllText(legacyPath));
            Assert.Contains(receipts, receipt => receipt.SchemaVersion == 3 && receipt.ReceiptSha256 is null);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ReadAll_RejectsSchemaFourReceiptWithoutIntegrityHash()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ReceiptPath)!);
            File.WriteAllText(
                plan.ReceiptPath,
                "{\"schemaVersion\":4,\"action\":\"Upload\",\"sourceCommit\":\"0123456789abcdef0123456789abcdef01234567\",\"targets\":[]}");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseReceiptStore().ReadAll(plan));

            Assert.Contains("required integrity SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ReadAll_AllowsExplicitSchemaThreeReceiptWithoutIntegrityHash()
    {
        var root = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ReceiptPath)!);
            File.WriteAllText(
                plan.ReceiptPath,
                "{\"schemaVersion\":3,\"action\":\"Upload\",\"sourceCommit\":\"0123456789abcdef0123456789abcdef01234567\",\"targets\":[]}");

            var receipt = Assert.Single(new AppleReleaseReceiptStore().ReadAll(plan));

            Assert.Equal(3, receipt.SchemaVersion);
            Assert.Null(receipt.ReceiptSha256);
        }
        finally
        {
            TryDelete(root);
        }
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void WriteAttempt_RejectsLinkedReceiptHistoryDirectory()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateSandbox();
        var outside = CreateSandbox();
        try
        {
            var plan = CreatePlan(root);
            Directory.CreateDirectory(Path.GetDirectoryName(plan.ReceiptHistoryPath)!);
            Directory.CreateSymbolicLink(plan.ReceiptHistoryPath, outside);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new AppleReleaseReceiptStore().WriteAttempt(
                    plan,
                    CreateReceipt(PowerForgeAppleReleaseAction.Upload, success: true)));
            Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(Path.Combine(root, "build", "powerforge", "apple", "receipts")))
                Directory.Delete(Path.Combine(root, "build", "powerforge", "apple", "receipts"));
            TryDelete(root);
            TryDelete(outside);
        }
    }
#endif

    private static PowerForgeAppleReleasePlan CreatePlan(string root)
        => new()
        {
            ProjectRoot = root,
            ReceiptPath = Path.Combine(root, "build", "powerforge", "apple", "release-receipt.json"),
            ReceiptHistoryPath = Path.Combine(root, "build", "powerforge", "apple", "receipts")
        };

    private static PowerForgeAppleReleaseReceipt CreateReceipt(
        PowerForgeAppleReleaseAction action,
        bool success)
        => new()
        {
            Action = action,
            SourceCommit = "0123456789abcdef0123456789abcdef01234567",
            Success = success
        };

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), $"powerforge-receipts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
