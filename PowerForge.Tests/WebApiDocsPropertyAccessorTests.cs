using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests
{
    public sealed class WebApiDocsPropertyAccessorTests
    {
        [Theory]
        [InlineData("PrivateSetter", "public Int32 PrivateSetter { get; private set; }")]
        [InlineData("PrivateGetter", "public Int32 PrivateGetter { private get; set; }")]
        [InlineData("ProtectedSetter", "public Int32 ProtectedSetter { get; protected set; }")]
        [InlineData("InternalSetter", "public Int32 InternalSetter { get; internal set; }")]
        [InlineData("ProtectedInternalSetter", "public Int32 ProtectedInternalSetter { get; protected internal set; }")]
        [InlineData("PrivateProtectedSetter", "public Int32 PrivateProtectedSetter { get; private protected set; }")]
        [InlineData("InitialValue", "public Int32 InitialValue { get; init; }")]
        [InlineData("PrivateInitialValue", "public Int32 PrivateInitialValue { get; private init; }")]
        [InlineData("Item", "public Int32 this[Int32 key] { get; private set; }")]
        [InlineData("Mutable", "public Int32 Mutable { get; set; }")]
        [InlineData("ReadOnly", "public Int32 ReadOnly { get; }")]
        [InlineData("WriteOnly", "public Int32 WriteOnly { set; }")]
        public void Generate_PreservesAccessorContractInJsonAndHtml(string name, string signature)
        {
            var root = Path.Combine(Path.GetTempPath(), "pf-web-accessors-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var xml = Path.Combine(root, "fixture.xml");
                File.WriteAllText(xml, "<doc><assembly><name>PowerForge.Tests</name></assembly><members/></doc>");
                var output = Path.Combine(root, "api");
                var options = new WebApiDocsOptions
                {
                    XmlPath = xml,
                    AssemblyPath = typeof(ApiAccessors.AccessorFixture).Assembly.Location,
                    OutputPath = output,
                    Format = "both",
                    IncludeUndocumentedTypes = true
                };
                options.IncludeNamespacePrefixes.Add("PowerForge.Tests.ApiAccessors");
                Assert.Equal(1, WebApiDocsGenerator.Generate(options).TypeCount);
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "types",
                    "powerforge-tests-apiaccessors-accessorfixture.json")));
                var member = json.RootElement.GetProperty("properties").EnumerateArray()
                    .Single(property => property.GetProperty("name").GetString() == name);
                Assert.Equal(signature, member.GetProperty("signature").GetString());
                var html = File.ReadAllText(Path.Combine(output, "types",
                    "powerforge-tests-apiaccessors-accessorfixture.html"));
                Assert.Contains(System.Net.WebUtility.HtmlEncode(signature), html, StringComparison.Ordinal);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

namespace PowerForge.Tests.ApiAccessors
{
    public class AccessorFixture
    {
        public int PrivateSetter { get; private set; }
        public int PrivateGetter { private get; set; }
        public int ProtectedSetter { get; protected set; }
        public int InternalSetter { get; internal set; }
        public int ProtectedInternalSetter { get; protected internal set; }
        public int PrivateProtectedSetter { get; private protected set; }
        public int InitialValue { get; init; }
        public int PrivateInitialValue { get; private init; }
        public int this[int key] { get => key; private set { } }
        public int Mutable { get; set; }
        public int ReadOnly => 0;
        public int WriteOnly { set { } }
    }
}
