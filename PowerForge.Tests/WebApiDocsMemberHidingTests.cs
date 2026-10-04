using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests
{
    public sealed class WebApiDocsMemberHidingTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Generate_PreservesNearestHiddenMembersAndDistinctOverloads(bool withXmlMembers)
        {
            WithGeneratedDocs(withXmlMembers, output =>
            {
                using var middle = ReadType(output, "middle");
                using var leaf = ReadType(output, "leaf");
                foreach (var document in new[] { middle, leaf })
                {
                    var type = document.RootElement;
                    var fluent = type.GetProperty("methods").EnumerateArray()
                        .Where(member => member.GetProperty("name").GetString() == "Fluent").ToArray();
                    Assert.Equal(2, fluent.Length);
                    var own = fluent.Single(member => member.GetProperty("returnType").GetString() == "HidingMiddle");
                    Assert.Equal("PowerForge.Tests.ApiHiding.HidingMiddle", own.GetProperty("declaringType").GetString());
                    Assert.Equal(document == leaf, own.GetProperty("isInherited").GetBoolean());
                    Assert.Equal("String", ParameterType(own));
                    Assert.Contains(fluent, member => ParameterType(member) == "Int32");

                    AssertMember(type, "properties", "Value", "HidingMiddle", "HidingMiddle");
                    var indexers = type.GetProperty("properties").EnumerateArray()
                        .Where(member => member.GetProperty("name").GetString() == "Item").ToArray();
                    Assert.Equal(2, indexers.Length);
                    Assert.Contains(indexers, member => member.GetProperty("returnType").GetString() == "String");
                    Assert.Contains(indexers, member => member.GetProperty("returnType").GetString() == "Boolean");
                    AssertMember(type, "fields", "Caption", "String", "HidingMiddle");
                    AssertMember(type, "events", "Changed", "Action", "HidingMiddle");
                }
                if (withXmlMembers)
                {
                    var fluent = middle.RootElement.GetProperty("methods").EnumerateArray()
                        .Single(member => member.GetProperty("name").GetString() == "Fluent" &&
                            member.GetProperty("returnType").GetString() == "HidingMiddle");
                    Assert.Equal("Derived fluent summary.", fluent.GetProperty("summary").GetString());
                }
            });
        }

        [Fact]
        public void Generate_DoesNotAssignPrivateHiderDocumentationToInheritedPublicMembers()
        {
            WithGeneratedDocs(true, output =>
            {
                using var document = ReadType(output, "private");
                var type = document.RootElement;
                Assert.DoesNotContain("Private secret marker", type.GetRawText(), StringComparison.Ordinal);
                AssertMember(type, "methods", "Fluent", "HidingBase", "HidingBase", "String");
                AssertMember(type, "properties", "Value", "HidingBase", "HidingBase");
                AssertMember(type, "fields", "Caption", "Int32", "HidingBase");
            });
        }

        private static void AssertMember(JsonElement type, string collection, string name, string returnType,
            string declaringType, string? parameterType = null)
        {
            var member = type.GetProperty(collection).EnumerateArray().Single(member =>
                member.GetProperty("name").GetString() == name &&
                (parameterType is null || ParameterType(member) == parameterType));
            Assert.Equal(returnType, member.GetProperty("returnType").GetString());
            Assert.Equal("PowerForge.Tests.ApiHiding." + declaringType, member.GetProperty("declaringType").GetString());
        }

        private static string? ParameterType(JsonElement member) =>
            member.GetProperty("parameters")[0].GetProperty("type").GetString()?.Split('.').Last();

        private static JsonDocument ReadType(string output, string suffix) => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(output, "types", "powerforge-tests-apihiding-hiding" + suffix + ".json")));

        private static void WithGeneratedDocs(bool withXmlMembers, Action<string> inspect)
        {
            var root = Path.Combine(Path.GetTempPath(), "pf-web-api-hiding-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var xml = Path.Combine(root, "fixture.xml");
                File.WriteAllText(xml, withXmlMembers ?
                    """
                    <doc><assembly><name>PowerForge.Tests</name></assembly><members>
                      <member name="T:PowerForge.Tests.ApiHiding.HidingBase"><summary>Base fixture.</summary></member>
                      <member name="T:PowerForge.Tests.ApiHiding.HidingMiddle"><summary>Middle fixture.</summary></member>
                      <member name="T:PowerForge.Tests.ApiHiding.HidingLeaf"><summary>Leaf fixture.</summary></member>
                      <member name="T:PowerForge.Tests.ApiHiding.HidingPrivate"><summary>Private hider fixture.</summary></member>
                      <member name="M:PowerForge.Tests.ApiHiding.HidingMiddle.Fluent(System.String)"><summary>Derived fluent summary.</summary></member>
                      <member name="P:PowerForge.Tests.ApiHiding.HidingMiddle.Value"><summary>Derived property.</summary></member>
                      <member name="P:PowerForge.Tests.ApiHiding.HidingMiddle.Item(System.String)"><summary>Derived indexer.</summary></member>
                      <member name="F:PowerForge.Tests.ApiHiding.HidingMiddle.Caption"><summary>Derived field.</summary></member>
                      <member name="E:PowerForge.Tests.ApiHiding.HidingMiddle.Changed"><summary>Derived event.</summary></member>
                      <member name="M:PowerForge.Tests.ApiHiding.HidingPrivate.Fluent(System.String)"><summary>Private secret marker method.</summary></member>
                      <member name="P:PowerForge.Tests.ApiHiding.HidingPrivate.Value"><summary>Private secret marker property.</summary></member>
                      <member name="F:PowerForge.Tests.ApiHiding.HidingPrivate.Caption"><summary>Private secret marker field.</summary></member>
                      <member name="E:PowerForge.Tests.ApiHiding.HidingPrivate.Changed"><summary>Private secret marker event.</summary></member>
                    </members></doc>
                    """ : "<doc><assembly><name>PowerForge.Tests</name></assembly><members/></doc>");
                var output = Path.Combine(root, "api");
                var options = new WebApiDocsOptions
                {
                    XmlPath = xml, AssemblyPath = typeof(ApiHiding.HidingMiddle).Assembly.Location,
                    OutputPath = output, Format = "json", IncludeUndocumentedTypes = !withXmlMembers
                };
                options.IncludeNamespacePrefixes.Add("PowerForge.Tests.ApiHiding");
                var result = WebApiDocsGenerator.Generate(options);
                Assert.Equal(4, result.TypeCount);
                inspect(output);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}

namespace PowerForge.Tests.ApiHiding
{
    public class HidingBase
    {
        public HidingBase Fluent(string value) => this;
        public HidingBase Fluent(int value) => this;
        public HidingBase Value => this;
        public int this[string key] => 0;
        public bool this[int key] => true;
        public int Caption;
        public event EventHandler Changed { add { } remove { } }
    }

    public class HidingMiddle : HidingBase
    {
        public new HidingMiddle Fluent(string value) => this;
        public new HidingMiddle Value => this;
        public new string this[string key] => key;
        public new string Caption = string.Empty;
        public new event Action Changed { add { } remove { } }
    }

    public class HidingLeaf : HidingMiddle { }

    public class HidingPrivate : HidingBase
    {
        private new string Fluent(string value) => value;
        private new string Value => Caption;
        private new string Caption = string.Empty;
        private new event Action Changed { add { } remove { } }
    }
}
