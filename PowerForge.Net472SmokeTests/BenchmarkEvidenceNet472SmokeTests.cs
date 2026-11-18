using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class BenchmarkEvidenceNet472SmokeTests
{
    [Fact]
    public void EvidenceCatalog_PublishesAndRereadsSchemaFiveLaneUnderNet472()
    {
        string root = Path.Combine(Path.GetTempPath(), "PowerForge.Evidence.Smoke." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string catalogPath = Path.Combine(root, "index.json");
            var request = new BenchmarkEvidenceUpdateRequest
            {
                Result = Result(),
                ComparisonId = "smoke-read",
                ResultPath = "windows.json",
                Profile = BenchmarkEvidenceProfile.Standard,
                Publish = true,
                Framework = "net472",
                Declarations = new BenchmarkDeclarationDocument
                {
                    Libraries =
                    [
                        new BenchmarkLibraryDeclaration { Id = "Managed", Package = "Managed", Version = "1.0.0", License = "MIT" },
                        new BenchmarkLibraryDeclaration { Id = "Other", Package = "Other", Version = "2.0.0", License = "LicenseRef-Other", Restriction = BenchmarkLibraryRestriction.AcceptanceRequired, Reason = "Needs a licence key." }
                    ],
                    Comparisons =
                    [
                        new BenchmarkComparisonDefinition { ComparisonId = "smoke-read", Area = "smoke", Title = "Smoke read", Subject = "Managed", Libraries = ["Managed", "Other", "Third"] }
                    ]
                }
            };
            request.Declarations.Libraries = [.. request.Declarations.Libraries, new BenchmarkLibraryDeclaration { Id = "Third", Package = "Third", Version = "3.0.0", License = "MIT" }];

            new BenchmarkEvidenceCatalogService().UpdateFile(catalogPath, request);
            string json = File.ReadAllText(catalogPath);
            BenchmarkEvidenceCatalog reread = new BenchmarkEvidenceCatalogService().ReadCatalog(catalogPath);

            Assert.Contains("\"profile\": \"standard\"", json);
            Assert.Contains("\"restriction\": \"acceptance-required\"", json);
            BenchmarkEvidenceEntry entry = Assert.Single(reread.Entries);
            Assert.Equal("smoke-read/windows/net472/standard", entry.LaneKey);
            Assert.True(Assert.Single(entry.Cells).Publishable);
            Assert.NotNull(entry.Cells[0].ConfidenceInterval);
        }
        finally { Directory.Delete(root, true); }
    }

    private static BenchmarkRunResult Result()
    {
        DateTimeOffset finished = DateTimeOffset.UtcNow.AddMinutes(-1);
        BenchmarkSample[] samples = Enumerable.Range(0, 12).Select(i => new BenchmarkSample
        {
            RunId = "smoke", Suite = "smoke", Scenario = "Read", Operation = "Run", Engine = "Managed", Library = "Managed",
            Host = "Desktop", Os = "Windows", RunMode = "standard", Iteration = i, Status = BenchmarkSampleStatus.Succeeded, DurationMs = 100 + i
        }).ToArray();
        return new BenchmarkRunResult
        {
            RunId = "smoke", Suite = "smoke", StartedUtc = finished.AddMinutes(-1), FinishedUtc = finished,
            Samples = samples,
            Summary = new BenchmarkSummaryService().Summarize(samples),
            Runner = new BenchmarkRunnerInfo { Kind = "rotated", Statistic = "median", Warmups = 3, Iterations = 12, Ordering = "rotated" },
            Validations = [new BenchmarkOutputValidation { Library = "Managed", Status = BenchmarkValidationStatus.Passed, Detail = "ok" }],
            Environment = new BenchmarkEnvironmentInfo
            {
                OsFamily = "Windows", OsDescription = "Windows 11", OsArchitecture = "X64", ProcessArchitecture = "X64",
                ProcessorName = "Smoke CPU", LogicalCoreCount = 8, RuntimeVersion = ".NET Framework 4.8", DotNetSdkVersion = "10.0.100", Runner = "PowerForge"
            },
            Metadata = new Dictionary<string, string>
            {
                ["gitSha"] = "0123456789abcdef0123456789abcdef01234567",
                ["gitWorktreeClean"] = "true"
            }
        };
    }
}
