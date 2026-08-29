using System.Reflection;

namespace PowerForge.Tests;

public sealed class PowerForgePublicBinaryCompatibilityTests
{
    [Fact]
    public void ModuleBuildResult_PreservesOriginalFourParameterConstructor()
    {
        var constructor = typeof(ModuleBuildResult).GetConstructor(new[]
        {
            typeof(string),
            typeof(string),
            typeof(ExportSet),
            typeof(ModuleOwnerNote[])
        });

        Assert.NotNull(constructor);
        var result = Assert.IsType<ModuleBuildResult>(constructor!.Invoke(new object?[]
        {
            "staging",
            "module.psd1",
            new ExportSet(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()),
            null
        }));
        Assert.Empty(result.FinalizedPayloadFiles);
    }

    [Fact]
    public void ArtefactBuilder_PreservesOriginalBuildWithFinalizerClrSignature()
    {
        var parameterTypes = new[]
        {
            typeof(ConfigurationArtefactSegment),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(string),
            typeof(IReadOnlyList<RequiredModuleReference>),
            typeof(Func<PackedArtefactFinalizationContext, IReadOnlyList<string>>),
            typeof(InformationConfiguration),
            typeof(DeliveryOptionsConfiguration),
            typeof(bool)
        };

        var method = typeof(ArtefactBuilder).GetMethod(
            nameof(ArtefactBuilder.BuildWithFinalizer),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: parameterTypes,
            modifiers: null);

        Assert.NotNull(method);
        Assert.Equal(typeof(ArtefactBuildResult), method!.ReturnType);
    }
}
