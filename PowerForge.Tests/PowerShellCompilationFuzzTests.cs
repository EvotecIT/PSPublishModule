using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellCompilationFuzzTests
{
    private static readonly string[] MinimizedParserBinderRegressions =
    {
        "function Get-OpenArray { @(",
        "function Get-OpenHashtable { @{ Key = 'value' ",
        "function Get-DanglingMember { $value. }",
        "function Get-UnclosedString { 'value }",
        "function Get-NestedBlock { if ($true) { foreach ($item in 1) { $item } }",
        ". '../outside.ps1'"
    };

    [Fact]
    public void ParserAndBinderFuzz_IsDeterministicBoundedAndDoesNotExecuteSource()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCompilationFuzz", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "fuzz.ps1");
            var random = new Random(0x5EED2026);
            var cases = MinimizedParserBinderRegressions
                .Concat(Enumerable.Range(0, 64).Select(_ => CreateMalformedCase(random)))
                .ToArray();
            var analyzer = new PowerShellCompilationAnalyzer();

            foreach (var source in cases)
            {
                File.WriteAllText(path, source);
                var first = analyzer.Analyze(new PowerShellCompilationSpec(path, PowerShellCompilationMode.Hybrid, targetFramework: "net8.0"));
                var second = analyzer.Analyze(new PowerShellCompilationSpec(path, PowerShellCompilationMode.Hybrid, targetFramework: "net8.0"));

                Assert.Equal(GetPlanSignature(first), GetPlanSignature(second));
                Assert.InRange(first.Files.Sum(static file => file.Diagnostics.Length + file.Units.Sum(static unit => unit.Diagnostics.Length)), 0, 512);
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SupportedArithmeticFuzz_MatchesPinnedPowerShellOracleAndStrictClr()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForgeCompilationFuzz", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        try
        {
            const int caseCount = 32;
            var random = new Random(0xA11CE);
            var functions = new StringBuilder();
            var invocations = new StringBuilder();
            var inputs = new int[caseCount];
            for (var index = 0; index < caseCount; index++)
            {
                var name = $"Get-Fuzz{index:D2}";
                var operand = random.Next(1, 19);
                inputs[index] = random.Next(20, 200);
                var operation = (index % 4) switch
                {
                    0 => $"$result += {operand}",
                    1 => $"$result -= {operand}",
                    2 => $"$result *= {operand}",
                    _ => $"$result += -{operand}"
                };
                functions.AppendLine($"function {name} {{ param([int] $Value); [int] $result = $Value; {operation}; return $result }}");
                invocations.AppendLine($"{name} {inputs[index]}");
            }

            var sourcePath = Path.Combine(root, "fuzz.ps1");
            var oraclePath = Path.Combine(root, "oracle.ps1");
            File.WriteAllText(sourcePath, functions.ToString());
            File.WriteAllText(oraclePath, functions.ToString() + invocations);
            var interpreted = new PowerShellCompilationSemanticOracleRunner().Observe(
                new PowerShellCompilationSemanticOracleRequest(
                    PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
                    oraclePath));
            var build = new PowerShellCompilationArtifactBuilder().Build(new PowerShellCompilationBuildSpec(
                sourcePath,
                output,
                "FuzzStrict",
                PowerShellCompilationArtifactKind.Library,
                PowerShellCompilationMode.Strict,
                allowUnreviewedDependencyResolution: true));

            Assert.True(build.Succeeded, build.Error + Environment.NewLine + build.BuildOutput);
            Assert.Equal(caseCount, interpreted.Success.Length);
            using var stream = File.OpenRead(build.ArtifactPath!);
            var context = new AssemblyLoadContext("FuzzStrict-" + Guid.NewGuid().ToString("N"), isCollectible: true);
            try
            {
                var type = context.LoadFromStream(stream).GetType("PowerForge.Compiled.FuzzStrictMethods", throwOnError: true)!;
                for (var index = 0; index < caseCount; index++)
                {
                    var method = type.GetMethod($"Get_Fuzz{index:D2}", BindingFlags.Public | BindingFlags.Static)!;
                    var actual = method.Invoke(null, new object[] { inputs[index] });
                    Assert.Equal(interpreted.Success[index].Value, Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture));
                    Assert.Equal("System.Int32", actual!.GetType().FullName);
                }
            }
            finally
            {
                context.Unload();
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string CreateMalformedCase(Random random)
    {
        const string alphabet = "abcXYZ0123$(){}[];,+-*/%.'\"#@ \r\n";
        var length = random.Next(1, 320);
        var builder = new StringBuilder(length + 32);
        if (random.Next(2) == 0)
            builder.Append("function Get-Fuzz { ");
        for (var index = 0; index < length; index++)
            builder.Append(alphabet[random.Next(alphabet.Length)]);
        return builder.ToString();
    }

    private static string GetPlanSignature(PowerShellCompilationPlan plan)
        => string.Join(
            "|",
            plan.Files.SelectMany(file =>
                file.Diagnostics.Select(diagnostic => $"F:{diagnostic.Code}:{diagnostic.FeatureId}:{diagnostic.Line}:{diagnostic.Column}")
                    .Concat(file.Units.Select(unit => $"U:{unit.Name}:{unit.Kind}:{unit.IsCompilable}")
                        .Concat(file.Units.SelectMany(unit => unit.Diagnostics.Select(diagnostic =>
                            $"D:{unit.Name}:{diagnostic.Code}:{diagnostic.FeatureId}:{diagnostic.Line}:{diagnostic.Column}"))))));
}
