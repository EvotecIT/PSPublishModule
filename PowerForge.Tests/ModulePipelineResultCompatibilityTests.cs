namespace PowerForge.Tests;

public sealed class ModulePipelineResultCompatibilityTests
{
    [Fact]
    public void Constructor_PreservesOriginalOwnerNotesSignature()
    {
        var constructors = typeof(ModulePipelineResult)
            .GetConstructors()
            .Select(static constructor => constructor.GetParameters().Select(static parameter => parameter.Name).ToArray())
            .ToArray();

        Assert.Contains(constructors, parameters =>
            parameters.Length > 0 &&
            string.Equals(parameters[^1], "ownerNotes", StringComparison.Ordinal));
        Assert.DoesNotContain(constructors, parameters =>
            parameters.Length > 0 &&
            string.Equals(parameters[^1], "typeAcceleratorSurfaceReport", StringComparison.Ordinal));

        var reportProperty = typeof(ModulePipelineResult).GetProperty(nameof(ModulePipelineResult.TypeAcceleratorSurfaceReport));
        Assert.NotNull(reportProperty);
        Assert.Null(reportProperty!.GetSetMethod(nonPublic: false));
        Assert.NotNull(reportProperty.GetSetMethod(nonPublic: true));
    }
}
