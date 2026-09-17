using System.Management.Automation;
using System.Reflection;
using PowerForge.Generated.Runtime;

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

    [Fact]
    public void PowerShellCompiledRegion_PreservesOriginalConstructorSignature()
    {
        var constructor = typeof(PowerShellCompiledRegion).GetConstructor(new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(string),
            typeof(string), typeof(string), typeof(IReadOnlyList<PowerShellCompilationParameter>),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int),
            typeof(IReadOnlyList<PowerShellCompilationSourceMapEntry>), typeof(PowerShellCompilationRegionGraph),
            typeof(string), typeof(IReadOnlyList<PowerShellCompiledRegionLocal>), typeof(bool), typeof(bool)
        });

        Assert.NotNull(constructor);
    }

    [Fact]
    public void PowerShellCompilationRegionCandidate_PreservesOriginalConstructorSignature()
    {
        var constructor = typeof(PowerShellCompilationRegionCandidate).GetConstructor(new[]
        {
            typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(string),
            typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int), typeof(bool),
            typeof(string), typeof(string), typeof(string), typeof(PowerShellCompilationRegionGraph),
            typeof(IReadOnlyList<PowerShellCompiledRegionLocal>), typeof(bool)
        });

        Assert.NotNull(constructor);
    }

    [Fact]
    public void PowerShellHybridRegionHost_PreservesOriginalCreateSignature()
    {
        var method = typeof(PowerShellHybridRegionHost).GetMethod(nameof(PowerShellHybridRegionHost.Create), new[]
        {
            typeof(PSModuleInfo), typeof(string), typeof(string), typeof(int), typeof(int),
            typeof(int[]), typeof(int[]), typeof(string[]), typeof(bool[]), typeof(string[])
        });

        Assert.NotNull(method);
    }

    [Fact]
    public void PowerShellHybridRegionHost_PreservesOriginalInstallSignature()
    {
        var method = typeof(PowerShellHybridRegionHost).GetMethod(nameof(PowerShellHybridRegionHost.TryInstallDeclaredFunction), new[]
        {
            typeof(PSModuleInfo), typeof(string), typeof(string), typeof(string), typeof(int), typeof(int),
            typeof(int[]), typeof(int[]), typeof(string[]), typeof(bool[]), typeof(string[]), typeof(string[]),
            typeof(int[]), typeof(string[])
        });

        Assert.NotNull(method);
    }
}
