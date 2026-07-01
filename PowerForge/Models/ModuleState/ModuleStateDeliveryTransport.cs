namespace PowerForge;

/// <summary>
/// Delivery engine used when a module-state plan is prepared or executed.
/// </summary>
public enum ModuleStateDeliveryTransport
{
    /// <summary>
    /// Use the existing private-module workflow and repository profile commands.
    /// </summary>
    PrivateModule,

    /// <summary>
    /// Use the managed C# install/update module engine.
    /// </summary>
    ManagedModule,

    /// <summary>
    /// Prefer the managed C# engine when a repository source URI or path is available, otherwise use compatibility transport.
    /// </summary>
    Auto
}
