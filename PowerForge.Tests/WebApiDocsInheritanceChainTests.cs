using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsInheritanceChainTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Generate_ResolvesImportedImplicitChainOrReportsItsMissingContract(bool documentedContract)
    {
        WithRoot(root =>
        {
            var assemblyPath = Path.Combine(root, "ImportedDerived.dll");
            var builder = new PersistedAssemblyBuilder(new AssemblyName("ImportedDerived"), typeof(object).Assembly);
            var type = builder.DefineDynamicModule("Main").DefineType("ImportedDerived", TypeAttributes.Public, typeof(WebImportedDocBase));
            type.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Virtual, typeof(void), Type.EmptyTypes)
                .GetILGenerator().Emit(OpCodes.Ret);
            type.CreateType();
            builder.Save(assemblyPath);
            File.Copy(typeof(WebImportedDocBase).Assembly.Location, Path.Combine(root, "PowerForge.Tests.dll"));
            File.WriteAllText(Path.Combine(root, "PowerForge.Tests.xml"), "<doc><members>" +
                "<member name=\"M:PowerForge.Tests.WebImportedDocBase.Run\"><inheritdoc/></member>" +
                (documentedContract ? "<member name=\"M:PowerForge.Tests.IWebImportedDocContract.Run\"><summary>Imported contract operation.</summary></member>" : "") +
                "</members></doc>");
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:ImportedDerived"><summary>Implementation.</summary></member>
                  <member name="M:ImportedDerived.Run"><inheritdoc/></member>
                </members></doc>
                """);
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = assemblyPath, OutputPath = Path.Combine(root, "api"),
                Format = "json", IncludeUndocumentedTypes = false
            });
            if (documentedContract)
            {
                Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Implicit inheritdoc", StringComparison.Ordinal));
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "api", "types", "importedderived.json")));
                Assert.Equal("Imported contract operation.", json.RootElement.GetProperty("methods").EnumerateArray()
                    .Single(method => method.GetProperty("name").GetString() == "Run").GetProperty("summary").GetString());
            }
            else
                Assert.Contains(result.Warnings, warning => warning.Contains("Implicit inheritdoc could not be resolved for M:PowerForge.Tests.WebImportedDocBase.Run", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Generate_ExplicitFrameworkTargetUsesBuiltInDocsWithoutInferredCandidatesOrTargetingPack()
    {
        WithRoot(root =>
        {
            var assemblyPath = Path.Combine(root, "ExplicitFramework.dll");
            var builder = new PersistedAssemblyBuilder(new AssemblyName("ExplicitFramework"), typeof(object).Assembly);
            var type = builder.DefineDynamicModule("Main").DefineType("ExplicitFramework", TypeAttributes.Public);
            var method = type.DefineMethod("Equals", MethodAttributes.Public, typeof(bool), new[] { typeof(int) });
            method.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
            method.GetILGenerator().Emit(OpCodes.Ret);
            type.CreateType();
            builder.Save(assemblyPath);
            var xml = Path.Combine(root, "docs.xml");
            File.WriteAllText(xml, """
                <doc><members>
                  <member name="T:ExplicitFramework"><summary>Comparison.</summary></member>
                  <member name="M:ExplicitFramework.Equals(System.Int32)"><inheritdoc cref="M:System.Object.Equals(System.Object)"/></member>
                </members></doc>
                """);
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                XmlPath = xml, AssemblyPath = assemblyPath, OutputPath = Path.Combine(root, "api"),
                Format = "json", IncludeUndocumentedTypes = false
            });
            Assert.DoesNotContain(result.Warnings, warning => warning.Contains("inheritdoc", StringComparison.OrdinalIgnoreCase));
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "api", "types", "explicitframework.json")));
            var comparison = json.RootElement.GetProperty("methods").EnumerateArray().Single(item => !item.GetProperty("isInherited").GetBoolean());
            Assert.Equal("Determines whether the specified object is equal to the current object.", comparison.GetProperty("summary").GetString());
            Assert.False(string.IsNullOrWhiteSpace(comparison.GetProperty("returns").GetString()));
        });
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-api-chain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}

public interface IWebImportedDocContract
{
    void Run();
}

public class WebImportedDocBase : IWebImportedDocContract
{
    public virtual void Run() { }
}
