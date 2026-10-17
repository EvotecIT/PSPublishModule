using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class AppleReleaseWorkflowTests
{
    [Theory]
    [InlineData("main", "powerforge.release.json")]
    [InlineData("master", "OfficeIMO.Studio/Build/officeimo.studio.apple.release.json")]
    public void WorkerLaunchAgent_passes_enrolled_branch_and_configuration(string branch, string config)
    {
        var values = new Dictionary<string, string>
        {
            ["LABEL"] = "com.example.worker", ["OWNER_ROOT"] = "/src/PowerForge", ["OWNER_COMMIT"] = new('a', 40),
            ["CONSUMER_ROOT"] = "/src/Studio", ["REPOSITORY"] = "example/Studio", ["DEFAULT_BRANCH"] = branch,
            ["POLICY_PATH"] = "/private/worker.json", ["CONFIG_PATH"] = config,
            ["STATE_ROOT"] = "/private/state", ["SCRATCH_ROOT"] = "/Volumes/Scratch/worker",
            ["LOG_ROOT"] = "/Users/example/Library/Logs/PowerForge"
        };
        var template = Read(FindRepoRoot(), "Build", "AppleTestFlightWorker.plist.template");
        foreach (var pair in values) template = template.Replace($"@@{pair.Key}@@", pair.Value, StringComparison.Ordinal);
        var document = XDocument.Parse(template);
        var dictionary = document.Root!.Element("dict")!;
        XElement Value(string key) => dictionary.Elements("key").Single(item => item.Value == key).ElementsAfterSelf().First();
        var arguments = Value("ProgramArguments").Elements("string").Select(item => item.Value).ToArray();
        string Argument(string name) => arguments[Array.IndexOf(arguments, name) + 1];
        Assert.Equal(branch, Argument("-ExpectedConsumerBranch"));
        Assert.Equal(config, Argument("--config"));
        Assert.Equal(values["CONSUMER_ROOT"], Argument("-ConsumerRoot"));
        Assert.Equal(values["LOG_ROOT"] + "/worker.log", Value("StandardOutPath").Value);
        Assert.Equal(values["LOG_ROOT"] + "/worker-error.log", Value("StandardErrorPath").Value);
        Assert.DoesNotContain("@@", template, StringComparison.Ordinal);
    }
}
