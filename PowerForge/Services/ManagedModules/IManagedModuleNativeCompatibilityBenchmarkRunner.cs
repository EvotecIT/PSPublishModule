namespace PowerForge;

internal interface IManagedModuleNativeCompatibilityBenchmarkRunner
{
    void Prepare(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine);

    ModuleDependencyInstallResult Run(
        ManagedModuleBenchmarkScenario scenario,
        ManagedModuleBenchmarkEngine engine);
}
