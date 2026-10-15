using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebApiDocsGeneratorContextLifetimeTests
{
    [Fact]
    public void ResolveDependencyType_PreservesTheInspectedContextWhenTheHostContainsTheSameType()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-type-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inspectedContext = new AssemblyLoadContext("Inspected API dependency", isCollectible: true);
        try
        {
            // API docs can inspect a private copy of a library also used by their own host.
            using var dependency = new MemoryStream(File.ReadAllBytes(typeof(WebApiDocsOptions).Assembly.Location), writable: false);
            var inspectedDependency = inspectedContext.LoadFromStream(dependency);
            var expectedType = inspectedDependency.GetType(typeof(WebApiDocsOptions).FullName!)!;
            var name = "ApiDocsIdentityConsumer" + Guid.NewGuid().ToString("N");
            var builder = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
            var adapter = builder.DefineDynamicModule(name).DefineType("ContextFixture.EngineAdapter",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            DefineRead(adapter, typeof(WebApiDocsOptions), "options");
            adapter.CreateType();
            var path = Path.Combine(root, name + ".dll");
            builder.Save(path);
            using var consumer = new MemoryStream(File.ReadAllBytes(path), writable: false);
            var inspectedAssembly = inspectedContext.LoadFromStream(consumer);
            var parameter = inspectedAssembly.GetType("ContextFixture.EngineAdapter")!.GetMethod("Read")!.GetParameters()[0];
            Assert.Same(expectedType, parameter.ParameterType);

            // Exercise the existing resolution boundary directly: later enrichment can repair
            // displayed names, but cannot make a foreign Type safe for identity matching or lazy loads.
            var resolver = typeof(WebApiDocsGenerator).GetMethod("ResolveType", BindingFlags.NonPublic | BindingFlags.Static)!;
            var resolved = Assert.IsAssignableFrom<Type>(resolver.Invoke(null, new object[] { inspectedAssembly, expectedType.FullName! }));

            Assert.Same(inspectedContext, AssemblyLoadContext.GetLoadContext(resolved.Assembly));
            Assert.Same(expectedType, resolved);
        }
        finally
        {
            inspectedContext.Unload();
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generate_ResolvesDependencyParametersOnlyWithinItsOwnContext(bool retireBeforeGeneration)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var retiredContext = new AssemblyLoadContext("Retired API documentation", isCollectible: true);
        Assembly? retiredAssembly = null;
        var retired = false;
        try
        {
            var suffix = Guid.NewGuid().ToString("N");
            var leafName = "ApiDocsContextLeaf" + suffix;
            var leafBuilder = new PersistedAssemblyBuilder(new AssemblyName(leafName), typeof(object).Assembly);
            var leafDefinition = leafBuilder.DefineDynamicModule(leafName).DefineType("ContextFixture.Base", TypeAttributes.Public);
            var leafConstructor = leafDefinition.DefineDefaultConstructor(MethodAttributes.Public);
            var leaf = leafDefinition.CreateType()!;
            var leafPath = Path.Combine(root, leafName + ".dll");
            leafBuilder.Save(leafPath);

            var dependencyName = "ApiDocsContextDependency" + suffix;
            var dependencyBuilder = new PersistedAssemblyBuilder(new AssemblyName(dependencyName), typeof(object).Assembly);
            var payload = dependencyBuilder.DefineDynamicModule(dependencyName).DefineType("ContextFixture.Payload",
                TypeAttributes.Public, leaf);
            var payloadConstructor = payload.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var constructorBody = payloadConstructor.GetILGenerator();
            constructorBody.Emit(OpCodes.Ldarg_0);
            constructorBody.Emit(OpCodes.Call, leafConstructor);
            constructorBody.Emit(OpCodes.Ret);
            var payloadType = payload.CreateType()!;
            var dependencyPath = Path.Combine(root, dependencyName + ".dll");
            dependencyBuilder.Save(dependencyPath);

            var consumerName = "ApiDocsContextConsumer" + suffix;
            var consumerBuilder = new PersistedAssemblyBuilder(new AssemblyName(consumerName), typeof(object).Assembly);
            var adapter = consumerBuilder.DefineDynamicModule(consumerName).DefineType("ContextFixture.Adapter",
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            DefineRead(adapter, payloadType, "payload");
            DefineRead(adapter, typeof(string), "text");
            adapter.CreateType();
            var consumerPath = Path.Combine(root, consumerName + ".dll");
            consumerBuilder.Save(consumerPath);

            // A foreign generation has not resolved this assembly's base dependency.
            // Probing it can fail or use the wrong type identity, even while that context is active.
            using (var snapshot = new MemoryStream(File.ReadAllBytes(dependencyPath), writable: false))
                retiredAssembly = retiredContext.LoadFromStream(snapshot);
            if (retireBeforeGeneration)
            {
                retiredContext.Unload();
                retired = true;
            }

            var xmlPath = Path.Combine(root, "consumer.xml");
            File.WriteAllText(xmlPath, $"""
                <doc><assembly><name>{consumerName}</name></assembly><members>
                  <member name="T:ContextFixture.Adapter"><summary>Reads dependency values.</summary></member>
                  <member name="M:ContextFixture.Adapter.Read(ContextFixture.Payload)"><summary>Reads a payload.</summary></member>
                  <member name="M:ContextFixture.Adapter.Read(System.String)"><summary>Reads text.</summary></member>
                </members></doc>
                """);
            var output = Path.Combine(root, "api");

            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                AssemblyPaths = new[] { consumerPath, dependencyPath, leafPath },
                XmlPath = xmlPath,
                OutputPath = output,
                Format = "json",
                IncludeUndocumentedTypes = false
            });

            Assert.Equal(1, result.TypeCount);
            Assert.Empty(result.Warnings);
            using var json = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(output, "types"), "*.json"))));
            var methods = json.RootElement.GetProperty("methods").EnumerateArray()
                .Where(method => method.GetProperty("name").GetString() == "Read").ToArray();
            Assert.Equal(2, methods.Length);
            Assert.Equal(new[] { "payload", "text" }, methods.Select(method =>
                method.GetProperty("parameters")[0].GetProperty("name").GetString()).OrderBy(name => name, StringComparer.Ordinal));
            GC.KeepAlive(retiredAssembly);
            GC.KeepAlive(retiredContext);
        }
        finally
        {
            if (!retired)
                retiredContext.Unload();
            GC.KeepAlive(retiredAssembly);
            GC.KeepAlive(retiredContext);
            Directory.Delete(root, recursive: true);
        }
    }

    private static void DefineRead(TypeBuilder adapter, Type parameter, string name)
    {
        var method = adapter.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static,
            typeof(void), new[] { parameter });
        method.DefineParameter(1, ParameterAttributes.None, name);
        method.GetILGenerator().Emit(OpCodes.Ret);
    }
}
